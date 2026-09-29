using System;
using Blocky.Compiler;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>Ref struct passed to every op — no boxing, no closures, no allocation (TDD §6.3).</summary>
    public ref struct OpContext
    {
        private readonly VmThread _thread;
        private readonly SlotEvaluator _slots;

        public readonly int Pc;
        public readonly Instruction Instruction;
        public readonly ReadOnlySpan<ParamValue> Params;
        public readonly float DeltaTime;
        public readonly float Now;

        /// <summary>Written by an op returning <see cref="OpResult.Jump"/>.</summary>
        public int NextPc;

        public VmThread Thread => _thread;
        public GameObject Target => _thread.Target;

        /// <summary>The scheduler running this thread — what <c>stop</c> and <c>reset timer</c> reach for. Never null in play.</summary>
        public readonly VmScheduler Scheduler;

        /// <summary>One persistent float slot per instruction (timers, progress) — TDD §6.3.</summary>
        public float Scratch
        {
            get => _thread.Scratch[Pc];
            set => _thread.Scratch[Pc] = value;
        }

        public OpContext(VmThread thread, int pc, Instruction instruction, ReadOnlySpan<ParamValue> parameters, float deltaTime, float now,
            SlotEvaluator slots, VmScheduler scheduler = null)
        {
            _thread = thread;
            _slots = slots;
            Scheduler = scheduler;
            Pc = pc;
            Instruction = instruction;
            Params = parameters;
            DeltaTime = deltaTime;
            Now = now;
            NextPc = -1;
        }

        /// <summary>
        /// Reads a true/false input. A slot is evaluated right now — so <c>repeat until &lt;mouse down?&gt;</c>
        /// sees the mouse as it is on this lap, not as it was when the program compiled. Empty slots read as false.
        /// </summary>
        public bool GetBool(int index) => _slots.Evaluate(Params[index], _thread);

        /// <summary>
        /// Reads a numeric input. **Every op should read its numbers through this, never <c>Params[i].Number</c>**:
        /// a learner can drop a reporter into any white oval, and only this resolves it. A typed-in number costs
        /// one switch; an empty slot reads as 0.
        /// </summary>
        public float GetNumber(int index) => _slots.EvaluateValue(Params[index], _thread).AsNumber();

        /// <summary>Reads a text input, running whatever reporter may be in it. An empty slot reads as "".</summary>
        public string GetText(int index) => _slots.EvaluateValue(Params[index], _thread).AsText();

        /// <summary>The whole value of an input, for an op that cares what it actually is.</summary>
        public BlockValue GetValue(int index) => _slots.EvaluateValue(Params[index], _thread);
    }
}
