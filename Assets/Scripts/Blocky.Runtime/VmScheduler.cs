using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Unity.Profiling;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Ticks every live thread once per frame from a single call (TDD §6.5). A flat instruction array plus
    /// explicit thread state, not async recursion — see TDD §6.1 for why.
    /// The learner's playback controls live here too, so every script in the scene obeys them together:
    /// <see cref="Pause"/> freezes scripts and their clock, <see cref="Step"/> lets each script run exactly one
    /// more block, <see cref="TimeScale"/> speeds timed blocks up, and <see cref="SaveMoment"/> /
    /// <see cref="RestoreMoment"/> let <see cref="Playback"/> step back. All of it runs on <c>dt</c>, so a run
    /// stays deterministic.
    /// </summary>
    public sealed class VmScheduler
    {
        private static readonly ProfilerMarker TickMarker = new("Blocky.VmScheduler.Tick");
        private static readonly ProfilerMarker StepMarker = new("Blocky.VmScheduler.Step");

        private readonly Predicate<VmThread> _isDone = t => t.State == ThreadState.Done; // cached once: RemoveAll every tick must not allocate

        private readonly IBlockOp[] _opTable;
        private readonly SlotEvaluator _slots;
        private readonly List<VmThread> _threads = new();
        private readonly List<VmThread> _pending = new();
        private readonly HashSet<(CompiledProgram, int)> _loggedFailures = new();
        private float _now;
        private float _timerOrigin;
        private bool _paused;
        private bool _stepping;

        public int InstructionBudget { get; set; } = 10_000;

        /// <summary>Multiplies every tick's <c>dt</c>: 2 makes a 2-second move take 1 second. Instant blocks are instant at any speed.</summary>
        public float TimeScale { get; set; } = 1f;

        /// <summary>True while scripts are frozen — including between steps.</summary>
        public bool IsPaused => _paused;

        /// <summary>True while a <see cref="Step"/> is still under way (a timed block can take several ticks).</summary>
        public bool IsStepping => _stepping;

        /// <summary>Fired when a thread exhausts its per-tick instruction budget (an infinite-loop guard, TDD §6.4).</summary>
        public event Action<VmThread> OnRunawayThread;

        /// <param name="conditionTable">Condition ops by opcode (<see cref="OpTableBuilder.BuildConditions"/>); without it every condition slot reads as false.</param>
        /// <param name="valueTable">Reporter ops by opcode (<see cref="OpTableBuilder.BuildValues"/>); without it every value slot reads as empty.</param>
        public VmScheduler(IBlockOp[] opTable, IConditionOp[] conditionTable = null, IValueOp[] valueTable = null)
        {
            _opTable = opTable;
            _slots = new SlotEvaluator(conditionTable, valueTable, this);
        }

        /// <summary>The script clock: seconds of un-paused, time-scaled run time since this scheduler was made.</summary>
        public float Now => _now;

        /// <summary>
        /// Scratch's timer: seconds on the script clock since the last <see cref="ResetTimer"/> (or since the start).
        /// It runs on the same clock as every timed block, so pausing the scene pauses the timer too.
        /// </summary>
        public float Timer => _now - _timerOrigin;

        /// <summary>Puts the timer back to zero — <c>sensing.reset_timer</c>.</summary>
        public void ResetTimer() => _timerOrigin = _now;

        /// <summary>Live threads. Finished ones are dropped at the end of each tick.</summary>
        public IReadOnlyList<VmThread> Threads => _threads;

        /// <summary>Whether any script is running or about to start.</summary>
        public bool HasWork
        {
            get
            {
                if (_pending.Count > 0) return true;
                foreach (var t in _threads)
                    if (t.State != ThreadState.Done) return true;
                return false;
            }
        }

        /// <summary>Threads started during a tick begin on the next tick, never mid-tick (TDD §6.4).</summary>
        public VmThread Start(CompiledProgram program, GameObject target, int entryPc)
        {
            var thread = new VmThread(program, target, entryPc);
            _pending.Add(thread);
            return thread;
        }

        /// <summary>
        /// The unfinished thread running the stack at <paramref name="entryPc"/> of <paramref name="program"/> on
        /// <paramref name="target"/> (started or about to start), or null. Runners ask here instead of keeping their
        /// own thread list, so a thread brought back by Step back is still found.
        /// </summary>
        public VmThread FindLive(GameObject target, CompiledProgram program, int entryPc)
        {
            foreach (var t in _threads)
                if (t.EntryPc == entryPc && IsLive(t, target, program)) return t;
            foreach (var t in _pending)
                if (t.EntryPc == entryPc && IsLive(t, target, program)) return t;
            return null;
        }

        /// <summary>Ends every thread of <paramref name="program"/> on <paramref name="target"/> — a runner shutting down.</summary>
        public void StopWhere(GameObject target, CompiledProgram program)
        {
            foreach (var t in _threads)
                if (IsLive(t, target, program)) t.State = ThreadState.Done;
            foreach (var t in _pending)
                if (IsLive(t, target, program)) t.State = ThreadState.Done;
        }

        private static bool IsLive(VmThread t, GameObject target, CompiledProgram program) =>
            t.State != ThreadState.Done && t.Program == program && t.Target == target;

        /// <summary>Disabling a runner halts its threads; re-enabling does not resume them (TDD §6.4) — call Start again.</summary>
        public void StopAll()
        {
            foreach (var t in _threads) t.State = ThreadState.Done;
            _threads.Clear();
            _pending.Clear();
            _stepping = false;
        }

        /// <summary>
        /// Ends every live script without touching the thread lists — what <c>stop [all]</c> does from inside a
        /// running block. <see cref="StopAll"/> clears the lists, which is not safe to do from inside a tick.
        /// </summary>
        public void StopAllThreads()
        {
            foreach (var t in _threads) t.State = ThreadState.Done;
            foreach (var t in _pending) t.State = ThreadState.Done;
        }

        /// <summary>Ends every script running on <paramref name="target"/> except <paramref name="except"/> — <c>stop [other scripts in this object]</c>.</summary>
        public void StopOtherThreadsOn(GameObject target, VmThread except)
        {
            foreach (var t in _threads)
                if (t != except && t.Target == target) t.State = ThreadState.Done;
            foreach (var t in _pending)
                if (t != except && t.Target == target) t.State = ThreadState.Done;
        }

        /// <summary>Freezes every script where it is. Script time stops too, so a half-finished wait or move resumes exactly where it left off.</summary>
        public void Pause()
        {
            _paused = true;
            _stepping = false;
        }

        /// <summary>Runs normally again after <see cref="Pause"/> or <see cref="Step"/>.</summary>
        public void Resume()
        {
            _paused = false;
            _stepping = false;
            foreach (var t in _threads) t.StepParked = false;
        }

        /// <summary>
        /// Pauses (if running) and lets every script run exactly one more block, then freezes again — over several
        /// ticks when that block takes time (a timed move, a wait). A script caught in the middle of a block just
        /// finishes it. Scripts that start during the step get one block too.
        /// </summary>
        public void Step()
        {
            _paused = true;
            _stepping = true;
            foreach (var t in _threads) GrantStep(t);
        }

        private static void GrantStep(VmThread t)
        {
            t.StepParked = false;
            t.StepBudget = t.IsMidBlock ? 0 : 1; // mid-block, finishing it *is* the step
        }

        /// <summary>Records every script's progress and the script clock, for <see cref="RestoreMoment"/>.</summary>
        public SchedulerMoment SaveMoment() => new(_now, _timerOrigin, Save(_threads), Save(_pending));

        /// <summary>
        /// Puts every script back exactly as it was at <paramref name="moment"/> — scripts that have finished since
        /// come back, scripts started since are ended — and leaves everything paused. The objects in the scene are
        /// not this class's to restore: see <see cref="WorldSnapshot.RestoreMoment"/>.
        /// </summary>
        public void RestoreMoment(SchedulerMoment moment)
        {
            foreach (var t in _threads) t.State = ThreadState.Done;
            foreach (var t in _pending) t.State = ThreadState.Done;
            _threads.Clear();
            _pending.Clear();

            foreach (var saved in moment.Threads)
            {
                saved.Restore();
                _threads.Add(saved.Thread);
            }
            foreach (var saved in moment.Pending)
            {
                saved.Restore();
                _pending.Add(saved.Thread);
            }

            _now = moment.Now;
            _timerOrigin = moment.TimerOrigin;
            _paused = true;
            _stepping = false;
        }

        private static ThreadMoment[] Save(List<VmThread> threads)
        {
            var saved = new ThreadMoment[threads.Count];
            for (var i = 0; i < threads.Count; i++) saved[i] = new ThreadMoment(threads[i]);
            return saved;
        }

        public void Tick(float dt)
        {
            using var _ = TickMarker.Auto();
            if (_paused && !_stepping) return; // frozen: script time stands still, so waits and timed blocks keep their place

            dt *= TimeScale;
            _now += dt;

            // Threads started since the last Tick begin now, at the top of this one — never mid-tick (TDD §6.4).
            if (_pending.Count > 0)
            {
                if (_stepping)
                    foreach (var t in _pending) GrantStep(t);
                _threads.AddRange(_pending);
                _pending.Clear();
            }

            for (var i = 0; i < _threads.Count; i++)
            {
                var t = _threads[i];
                if (t.State == ThreadState.Done) continue;
                if (t.Target == null) { t.State = ThreadState.Done; continue; }
                if (t.StepParked) continue;
                if (t.State == ThreadState.Sleeping && _now < t.WakeAt) continue;

                var budget = InstructionBudget;
                t.State = ThreadState.Running;

                while (t.State == ThreadState.Running && budget-- > 0)
                    Step(t, dt);

                if (budget < 0) OnRunawayThread?.Invoke(t);
            }

            if (_stepping && EveryThreadParked()) _stepping = false; // paused again, each script one block further on
            _threads.RemoveAll(_isDone);
        }

        private bool EveryThreadParked()
        {
            foreach (var t in _threads)
                if (t.State != ThreadState.Done && !t.StepParked) return false;
            return true;
        }

        private void Step(VmThread t, float dt)
        {
            using var _ = StepMarker.Auto();
            if (t.FrameCount > 0)
            {
                ref var topFrame = ref t.TopFrame();
                if (t.Pc == topFrame.ExitPc)
                {
                    // Loop-yield rule (TDD §6.4): force one frame yield per lap that never yielded on its own,
                    // so a zero-yield loop body can't hang the game frame. Only applies to loop frames — a
                    // one-shot if_else skip-frame shouldn't add a hidden yield to every branch taken.
                    var forceYield = topFrame.IsLoop && !topFrame.YieldedThisLap;
                    topFrame.YieldedThisLap = false;
                    t.Pc = topFrame.OwnerPc; // hand control back to the owning C-block op; it decides: loop again or fall through
                    if (forceYield) t.State = ThreadState.YieldedFrame;
                    return;
                }
            }

            if (t.Pc < 0 || t.Pc >= t.ExitPc)
            {
                t.State = ThreadState.Done; // ran off the end of its own script, not just the end of the program
                return;
            }

            if (_stepping && !t.IsMidBlock)
            {
                if (t.StepBudget <= 0)
                {
                    t.StepParked = true; // wait here, on the threshold of the next block, for the next Step
                    t.State = ThreadState.YieldedFrame;
                    return;
                }
                t.StepBudget--;
            }

            var instr = t.Program.Code[t.Pc];
            var span = new ReadOnlySpan<ParamValue>(t.Program.ParamTable, instr.ParamOffset, instr.ParamCount);
            var ctx = new OpContext(t, t.Pc, instr, span, dt, _now, _slots, this);
            t.ActivePc = t.Pc;

            OpResult result;
            try
            {
                result = _opTable[instr.Opcode].Execute(ref ctx);
            }
            catch (Exception ex)
            {
                LogFailure(t, instr, ex.Message);
                t.State = ThreadState.Done;
                return;
            }

            t.ResumePc = result == OpResult.Retry ? t.Pc : -1;

            switch (result)
            {
                case OpResult.Continue:
                    t.Pc++;
                    break;
                case OpResult.YieldFrame:
                    t.Pc++;
                    if (t.State != ThreadState.Sleeping) t.State = ThreadState.YieldedFrame;
                    MarkAllFramesYielded(t);
                    break;
                case OpResult.Retry:
                    if (t.State != ThreadState.Sleeping) t.State = ThreadState.YieldedFrame;
                    MarkAllFramesYielded(t);
                    break;
                case OpResult.Jump:
                    t.Pc = ctx.NextPc;
                    break;
                case OpResult.Fail:
                    LogFailure(t, instr, "op returned Fail");
                    t.State = ThreadState.Done;
                    break;
            }
        }

        private static void MarkAllFramesYielded(VmThread t)
        {
            for (var i = 0; i < t.FrameCount; i++)
                t.Frames[i].YieldedThisLap = true;
        }

        private void LogFailure(VmThread t, Instruction instr, string message)
        {
            // One log per failure site per session (TDD §10.3) — runtime spam is how real errors get missed.
            if (!_loggedFailures.Add((t.Program, t.Pc))) return;
            var nodeId = t.Program.DebugNodeIds[instr.SourceNodeId];
            Debug.LogWarning($"Blocky: block '{nodeId}' failed: {message}");
        }
    }

    /// <summary>Every script's progress and the script clock at one point in time, from <see cref="VmScheduler.SaveMoment"/>.</summary>
    public sealed class SchedulerMoment
    {
        internal readonly float Now;
        internal readonly float TimerOrigin;
        internal readonly ThreadMoment[] Threads;
        internal readonly ThreadMoment[] Pending;

        internal SchedulerMoment(float now, float timerOrigin, ThreadMoment[] threads, ThreadMoment[] pending)
        {
            Now = now;
            TimerOrigin = timerOrigin;
            Threads = threads;
            Pending = pending;
        }
    }
}
