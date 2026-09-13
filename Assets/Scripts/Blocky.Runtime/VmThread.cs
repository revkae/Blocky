using System;
using Blocky.Compiler;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// TDD's "Thread" (renamed to avoid colliding with <see cref="System.Threading.Thread"/>). One live
    /// execution of one stack. Pooling is deferred to the Milestone 7 profiling pass (TDD §11.2) — v1 favors
    /// correctness; a thread is a plain allocation for now.
    /// </summary>
    public sealed class VmThread
    {
        public const int MaxNestingDepth = 16;

        public readonly CompiledProgram Program;
        public readonly GameObject Target;
        public int Pc;
        public readonly Frame[] Frames = new Frame[MaxNestingDepth];
        public int FrameCount;
        public readonly float[] Scratch;
        public ThreadState State;
        public float WakeAt;
        public int Generation;

        public VmThread(CompiledProgram program, GameObject target, int entryPc)
        {
            Program = program;
            Target = target;
            Pc = entryPc;
            Scratch = new float[program.Code.Length];
            State = entryPc >= 0 && entryPc < program.Code.Length ? ThreadState.Running : ThreadState.Done;
        }

        public void PushFrame(Frame frame)
        {
            if (FrameCount >= Frames.Length)
                throw new InvalidOperationException("Nesting depth exceeded thread frame capacity (soft-capped at 12 in the editor, TDD §8.4).");
            Frames[FrameCount++] = frame;
        }

        public void PopFrame() => FrameCount--;

        public ref Frame TopFrame() => ref Frames[FrameCount - 1];
    }
}
