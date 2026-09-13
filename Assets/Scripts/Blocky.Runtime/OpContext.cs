using System;
using Blocky.Compiler;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>Ref struct passed to every op — no boxing, no closures, no allocation (TDD §6.3).</summary>
    public ref struct OpContext
    {
        private readonly VmThread _thread;

        public readonly int Pc;
        public readonly Instruction Instruction;
        public readonly ReadOnlySpan<ParamValue> Params;
        public readonly float DeltaTime;
        public readonly float Now;

        /// <summary>Written by an op returning <see cref="OpResult.Jump"/>.</summary>
        public int NextPc;

        public VmThread Thread => _thread;
        public GameObject Target => _thread.Target;

        /// <summary>One persistent float slot per instruction (timers, progress) — TDD §6.3.</summary>
        public float Scratch
        {
            get => _thread.Scratch[Pc];
            set => _thread.Scratch[Pc] = value;
        }

        public OpContext(VmThread thread, int pc, Instruction instruction, ReadOnlySpan<ParamValue> parameters, float deltaTime, float now)
        {
            _thread = thread;
            Pc = pc;
            Instruction = instruction;
            Params = parameters;
            DeltaTime = deltaTime;
            Now = now;
            NextPc = -1;
        }
    }
}
