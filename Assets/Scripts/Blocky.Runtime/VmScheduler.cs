using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Unity.Profiling;
using UnityEngine;

// The statics here are fixed values that never change, so there is nothing to reset between Play sessions.
#pragma warning disable UAL0010, UAL0013

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
        private readonly List<VmThread> _started = new();  // reused each tick: the threads to report as started
        private readonly List<VmThread> _finished = new(); // reused each tick: the threads to report as finished
        private readonly HashSet<(CompiledProgram, int)> _loggedFailures = new();
        private float _now;
        private float _timerOrigin;
        private bool _paused;
        private bool _stepping;
        private GameObject _stepOnly; // set while a step is confined to one object's scripts; null means every script steps

        // Objects paused on their own (the run bar acts on the open object); every other object keeps running.
        // A paused object's scripts run on their own clock — the script clock minus the time it has spent paused —
        // so a wait or a timed move resumes exactly where it left off, just as it does when the whole scene pauses.
        private readonly HashSet<GameObject> _pausedObjects = new();
        private readonly Dictionary<GameObject, float> _lag = new();

        public int InstructionBudget { get; set; } = 10_000;

        /// <summary>Multiplies every tick's <c>dt</c>: 2 makes a 2-second move take 1 second. Instant blocks are instant at any speed.</summary>
        public float TimeScale { get; set; } = 1f;

        /// <summary>True while the whole scene is frozen — including between steps.</summary>
        public bool IsPaused => _paused;

        /// <summary>True while <paramref name="target"/>'s scripts are frozen, by a pause of the scene or of that object alone.</summary>
        public bool IsPausedFor(GameObject target) => _paused || (target != null && _pausedObjects.Contains(target));

        /// <summary>The script clock as <paramref name="target"/> sees it: without the time that object has spent paused.</summary>
        private float NowOf(GameObject target) => _lag.Count > 0 && _lag.TryGetValue(target, out var lag) ? _now - lag : _now;

        /// <summary>True while a <see cref="Step"/> is still under way (a timed block can take several ticks).</summary>
        public bool IsStepping => _stepping;

        /// <summary>Fired when a thread exhausts its per-tick instruction budget (an infinite-loop guard, TDD §6.4).</summary>
        public event Action<VmThread> OnRunawayThread;

        /// <summary>A thread began running — at the top of the tick after it was started (TDD §6.4).</summary>
        public event Action<VmThread> ThreadStarted;

        /// <summary>
        /// A thread that had started ended by itself during a tick — see <see cref="VmThread.EndReason"/>. Not raised
        /// for threads ended from outside: <see cref="StopAll"/>, <see cref="StopWhere"/>, a restart by the thread's own event.
        /// </summary>
        public event Action<VmThread> ThreadFinished;

        /// <summary>
        /// A thread finished by itself (<see cref="ThreadFinished"/>) and left nothing running or waiting to start.
        /// Never raised when everything was ended from outside — <see cref="StopAll"/>, an edit, disabled objects.
        /// </summary>
        public event Action AllThreadsFinished;

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

        /// <summary>Whether a script on <paramref name="target"/> is running or about to start.</summary>
        public bool HasWorkOn(GameObject target)
        {
            foreach (var t in _pending)
                if (t.Target == target && t.State != ThreadState.Done) return true;
            foreach (var t in _threads)
                if (t.Target == target && t.State != ThreadState.Done) return true;
            return false;
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
            _stepOnly = null;
            _pausedObjects.Clear();
            _lag.Clear();
        }

        /// <summary>
        /// Ends every script of <paramref name="target"/> and un-pauses it — the run bar's Stop for one object.
        /// Not for use from inside a tick (it edits the thread lists, like <see cref="StopAll"/>).
        /// </summary>
        public void StopObject(GameObject target)
        {
            foreach (var t in _threads)
                if (t.Target == target) t.State = ThreadState.Done;
            foreach (var t in _pending)
                if (t.Target == target) t.State = ThreadState.Done;
            _threads.RemoveAll(t => t.Target == target);
            _pending.RemoveAll(t => t.Target == target);

            _pausedObjects.Remove(target);
            _lag.Remove(target);
            if (_stepOnly == target)
            {
                _stepping = false;
                _stepOnly = null;
            }
        }

        /// <summary>
        /// Ends every live script without touching the thread lists — what <c>stop [all]</c> does from inside a
        /// running block. <see cref="StopAll"/> clears the lists, which is not safe to do from inside a tick.
        /// </summary>
        public void StopAllThreads()
        {
            foreach (var t in _threads) StopByBlock(t);
            foreach (var t in _pending) StopByBlock(t);
        }

        /// <summary>Ends every script running on <paramref name="target"/> except <paramref name="except"/> — <c>stop [other scripts in this object]</c>.</summary>
        public void StopOtherThreadsOn(GameObject target, VmThread except)
        {
            foreach (var t in _threads)
                if (t != except && t.Target == target) StopByBlock(t);
            foreach (var t in _pending)
                if (t != except && t.Target == target) StopByBlock(t);
        }

        private static void StopByBlock(VmThread t)
        {
            if (t.State == ThreadState.Done) return;
            t.EndReason ??= ScriptEndReason.StoppedByBlock;
            t.State = ThreadState.Done;
        }

        /// <summary>
        /// Freezes every script where it is — or, with <paramref name="only"/>, just that object's scripts while the
        /// rest carry on. Script time stops too, so a half-finished wait or move resumes exactly where it left off.
        /// </summary>
        public void Pause(GameObject only = null)
        {
            if (only == null)
            {
                _paused = true;
                _stepping = false;
                _stepOnly = null;
                return;
            }

            _pausedObjects.Add(only);
            if (_stepOnly == only)
            {
                _stepping = false;
                _stepOnly = null;
            }
        }

        /// <summary>Runs normally again after <see cref="Pause"/> or <see cref="Step"/> — everything, or with <paramref name="only"/>, just that object.</summary>
        public void Resume(GameObject only = null)
        {
            if (only == null)
            {
                _paused = false;
                _stepping = false;
                _stepOnly = null;
                _pausedObjects.Clear();
                foreach (var t in _threads) t.StepParked = false;
                return;
            }

            _pausedObjects.Remove(only);
            if (_stepOnly == only)
            {
                _stepping = false;
                _stepOnly = null;
            }
            foreach (var t in _threads)
                if (t.Target == only) t.StepParked = false;
        }

        /// <summary>
        /// Pauses (if running) and lets every script run exactly one more block, then freezes again — over several
        /// ticks when that block takes time (a timed move, a wait). A script caught in the middle of a block just
        /// finishes it. Scripts that start during the step get one block too.
        /// With <paramref name="only"/>, just that object pauses and steps; every other object carries on running.
        /// </summary>
        public void Step(GameObject only = null)
        {
            if (only == null) _paused = true;
            else _pausedObjects.Add(only);
            _stepping = true;
            _stepOnly = only;
            foreach (var t in _threads)
                if (Steps(t)) GrantStep(t);
        }

        private bool Steps(VmThread t) => _stepOnly == null || t.Target == _stepOnly;

        private static void GrantStep(VmThread t)
        {
            t.StepParked = false;
            t.StepBudget = t.IsMidBlock ? 0 : 1; // mid-block, finishing it *is* the step
        }

        /// <summary>
        /// Records every script's progress and the script clock, for <see cref="RestoreMoment"/> — or, with
        /// <paramref name="only"/>, just that object's scripts and its own clock.
        /// </summary>
        public SchedulerMoment SaveMoment(GameObject only = null) => only == null
            ? new SchedulerMoment(_now, _timerOrigin, Save(_threads, null), Save(_pending, null), null)
            : new SchedulerMoment(NowOf(only), _timerOrigin, Save(_threads, only), Save(_pending, only), only);

        /// <summary>
        /// Puts every script back exactly as it was at <paramref name="moment"/> — scripts that have finished since
        /// come back, scripts started since are ended — and leaves everything paused (for a moment of one object,
        /// just that object; the rest of the scene is left running). The objects in the scene are
        /// not this class's to restore: see <see cref="WorldSnapshot.RestoreMoment"/>.
        /// </summary>
        public void RestoreMoment(SchedulerMoment moment)
        {
            if (moment.Only != null)
            {
                RestoreObjectMoment(moment);
                return;
            }

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
            _stepOnly = null;
            _pausedObjects.Clear(); // the whole scene's clock went back, so every private clock starts over with it
            _lag.Clear();
        }

        private void RestoreObjectMoment(SchedulerMoment moment)
        {
            var only = moment.Only;
            foreach (var t in _threads)
                if (t.Target == only) t.State = ThreadState.Done;
            foreach (var t in _pending)
                if (t.Target == only) t.State = ThreadState.Done;
            _threads.RemoveAll(t => t.Target == only);
            _pending.RemoveAll(t => t.Target == only);

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

            _lag[only] = _now - moment.Now; // this object's clock goes back; the scene's keeps running
            _pausedObjects.Add(only);
            if (_stepOnly == only)
            {
                _stepping = false;
                _stepOnly = null;
            }
        }

        private static ThreadMoment[] Save(List<VmThread> threads, GameObject only)
        {
            var count = 0;
            foreach (var t in threads)
                if (only == null || t.Target == only) count++;

            var saved = new ThreadMoment[count];
            var next = 0;
            foreach (var t in threads)
                if (only == null || t.Target == only) saved[next++] = new ThreadMoment(t);
            return saved;
        }

        /// <summary>Adds the time every object paused on its own has been paused for this tick to its private lag.</summary>
        private void AccrueLag(float dt)
        {
            foreach (var go in _pausedObjects)
            {
                if (_stepping && (_stepOnly == null || _stepOnly == go)) continue; // being stepped: its clock runs
                _lag[go] = (_lag.TryGetValue(go, out var lag) ? lag : 0f) + dt;
            }
        }

        /// <summary>Whether <paramref name="t"/> is held still this tick: paused with its object or the scene, and not the one being stepped.</summary>
        private bool IsFrozen(VmThread t)
        {
            if (_stepping && Steps(t)) return false;
            return _paused || _pausedObjects.Contains(t.Target);
        }

        public void Tick(float dt)
        {
            using var _ = TickMarker.Auto();
            if (_paused && !_stepping) return; // frozen: script time stands still, so waits and timed blocks keep their place

            dt *= TimeScale;
            _now += dt;
            if (_pausedObjects.Count > 0) AccrueLag(dt);

            // Threads started since the last Tick begin now, at the top of this one — never mid-tick (TDD §6.4).
            if (_pending.Count > 0)
            {
                if (_stepping)
                    foreach (var t in _pending)
                        if (Steps(t)) GrantStep(t);
                foreach (var t in _pending)
                {
                    if (t.State == ThreadState.Done) continue; // stopped before it ever ran
                    t.Announced = true;
                    _started.Add(t);
                }
                _threads.AddRange(_pending);
                _pending.Clear();
                Report(_started, ThreadStarted);
            }

            for (var i = 0; i < _threads.Count; i++)
            {
                var t = _threads[i];
                if (t.State == ThreadState.Done) continue;
                if (t.Target == null) { t.State = ThreadState.Done; continue; }
                if (t.StepParked) continue;
                if (IsFrozen(t)) continue;
                if (t.State == ThreadState.Sleeping && NowOf(t.Target) < t.WakeAt) continue;

                var budget = InstructionBudget;
                t.State = ThreadState.Running;

                while (t.State == ThreadState.Running && budget-- > 0)
                    Step(t, dt);

                if (budget < 0) OnRunawayThread?.Invoke(t);
            }

            if (_stepping && EveryThreadParked()) _stepping = false; // paused again, each script one block further on

            foreach (var t in _threads)
                if (t.State == ThreadState.Done && t.EndReason.HasValue && t.Announced) _finished.Add(t);
            _threads.RemoveAll(_isDone);
            var anyFinished = _finished.Count > 0;
            Report(_finished, ThreadFinished);

            // Asked after the reports: a listener may have started something new.
            if (anyFinished && !HasWork)
            {
                try { AllThreadsFinished?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        /// <summary>Tells listeners about <paramref name="threads"/> once the lists are settled, then empties it. A listener's exception never stops the VM.</summary>
        private static void Report(List<VmThread> threads, Action<VmThread> listeners)
        {
            if (threads.Count == 0) return;
            if (listeners != null)
                foreach (var t in threads)
                {
                    try { listeners(t); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            threads.Clear();
        }

        private bool EveryThreadParked()
        {
            foreach (var t in _threads)
                if (t.State != ThreadState.Done && Steps(t) && !t.StepParked) return false;
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
                    if (topFrame.IsCall)
                    {
                        t.ReturnFromCall(); // a custom block finished: carry on after the block that ran it (ADR-029)
                        return;
                    }

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

            if (t.Pc < 0 || t.Pc >= t.EndPc)
            {
                t.EndReason = ScriptEndReason.Completed;
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
            var op = _opTable[instr.Opcode];
            if (op == null)
            {
                LogFailure(t, instr, "no op is bound to this block (the console said which when the game started)");
                t.EndReason = ScriptEndReason.Failed;
                t.State = ThreadState.Done;
                return;
            }

            var span = new ReadOnlySpan<ParamValue>(t.Program.ParamTable, instr.ParamOffset, instr.ParamCount);
            var ctx = new OpContext(t, t.Pc, instr, span, dt, NowOf(t.Target), _slots, this);
            t.ActivePc = t.Pc;

            OpResult result;
            try
            {
                result = op.Execute(ref ctx);
            }
            catch (Exception ex)
            {
                LogFailure(t, instr, ex.Message);
                t.EndReason = ScriptEndReason.Failed;
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
                    t.MarkFramesYielded();
                    break;
                case OpResult.Retry:
                    if (t.State != ThreadState.Sleeping) t.State = ThreadState.YieldedFrame;
                    t.MarkFramesYielded();
                    break;
                case OpResult.Jump:
                    t.Pc = ctx.NextPc;
                    break;
                case OpResult.Fail:
                    LogFailure(t, instr, "op returned Fail");
                    t.EndReason = ScriptEndReason.Failed;
                    t.State = ThreadState.Done;
                    break;
            }
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
        internal readonly GameObject Only; // the one object this moment covers (Now is then its own clock), or null for the whole scene

        internal SchedulerMoment(float now, float timerOrigin, ThreadMoment[] threads, ThreadMoment[] pending, GameObject only)
        {
            Now = now;
            TimerOrigin = timerOrigin;
            Threads = threads;
            Pending = pending;
            Only = only;
        }
    }
}
