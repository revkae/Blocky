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
    /// </summary>
    public sealed class VmScheduler
    {
        private static readonly ProfilerMarker TickMarker = new("Blocky.VmScheduler.Tick");
        private static readonly ProfilerMarker StepMarker = new("Blocky.VmScheduler.Step");

        private readonly IBlockOp[] _opTable;
        private readonly List<VmThread> _threads = new();
        private readonly List<VmThread> _pending = new();
        private readonly HashSet<(CompiledProgram, int)> _loggedFailures = new();
        private float _now;

        public int InstructionBudget { get; set; } = 10_000;

        /// <summary>Fired when a thread exhausts its per-tick instruction budget (an infinite-loop guard, TDD §6.4).</summary>
        public event Action<VmThread> OnRunawayThread;

        public VmScheduler(IBlockOp[] opTable)
        {
            _opTable = opTable;
        }

        public IReadOnlyList<VmThread> Threads => _threads;

        /// <summary>Threads started during a tick begin on the next tick, never mid-tick (TDD §6.4).</summary>
        public VmThread Start(CompiledProgram program, GameObject target, int entryPc)
        {
            var thread = new VmThread(program, target, entryPc);
            _pending.Add(thread);
            return thread;
        }

        /// <summary>Disabling a runner halts its threads; re-enabling does not resume them (TDD §6.4) — call Start again.</summary>
        public void StopAll()
        {
            foreach (var t in _threads) t.State = ThreadState.Done;
            _threads.Clear();
            _pending.Clear();
        }

        public void Tick(float dt)
        {
            using var _ = TickMarker.Auto();
            _now += dt;

            // Threads started since the last Tick begin now, at the top of this one — never mid-tick (TDD §6.4).
            if (_pending.Count > 0)
            {
                _threads.AddRange(_pending);
                _pending.Clear();
            }

            for (var i = 0; i < _threads.Count; i++)
            {
                var t = _threads[i];
                if (t.State == ThreadState.Done) continue;
                if (t.Target == null) { t.State = ThreadState.Done; continue; }
                if (t.State == ThreadState.Sleeping && _now < t.WakeAt) continue;

                var budget = InstructionBudget;
                t.State = ThreadState.Running;

                while (t.State == ThreadState.Running && budget-- > 0)
                    Step(t, dt);

                if (budget < 0) OnRunawayThread?.Invoke(t);
            }
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

            if (t.Pc < 0 || t.Pc >= t.Program.Code.Length)
            {
                t.State = ThreadState.Done;
                return;
            }

            var instr = t.Program.Code[t.Pc];
            var span = new ReadOnlySpan<ParamValue>(t.Program.ParamTable, instr.ParamOffset, instr.ParamCount);
            var ctx = new OpContext(t, t.Pc, instr, span, dt, _now);

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
}
