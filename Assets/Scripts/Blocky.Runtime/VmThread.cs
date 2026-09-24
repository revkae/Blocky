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

        /// <summary>Where this thread's stack starts in <see cref="Program"/> — together with the two above, which stack it is running.</summary>
        public readonly int EntryPc;

        /// <summary>
        /// The pc just past this stack's last block. Reaching it ends the thread: every stack of a program is
        /// emitted into one instruction array, so a script that simply runs out of blocks would otherwise carry on
        /// into whatever script was compiled after it (ADR-019).
        /// </summary>
        public readonly int ExitPc;

        public int Pc;
        public readonly Frame[] Frames = new Frame[MaxNestingDepth];
        public int FrameCount;
        public readonly float[] Scratch;
        public ThreadState State;
        public float WakeAt;
        public int Generation;

        /// <summary>The pc of the block this thread most recently started — the one the editor lights up. -1 before the first block.</summary>
        public int ActivePc = -1;

        /// <summary>The pc whose op last returned <see cref="OpResult.Retry"/> (a timed block, a wait), or -1.</summary>
        public int ResumePc = -1;

        /// <summary>Step mode: how many new blocks this thread may still start before it parks.</summary>
        public int StepBudget;

        /// <summary>Step mode: reached the start of a block with no budget left, and waits there for the next step.</summary>
        public bool StepParked;

        public VmThread(CompiledProgram program, GameObject target, int entryPc)
        {
            Program = program;
            Target = target;
            EntryPc = entryPc;
            ExitPc = program.ExitPcFor(entryPc);
            Pc = entryPc;
            Scratch = new float[program.Code.Length];
            State = entryPc >= 0 && entryPc < ExitPc ? ThreadState.Running : ThreadState.Done;
        }

        /// <summary>Part-way through a block that takes time: the next op call continues it rather than starting a new one.</summary>
        public bool IsMidBlock => ResumePc >= 0 && ResumePc == Pc;

        /// <summary>The id of the block at <see cref="ActivePc"/> (the node the editor shows), or null before the first block.</summary>
        public string ActiveNodeId =>
            ActivePc >= 0 && ActivePc < Program.Code.Length ? Program.DebugNodeIds[Program.Code[ActivePc].SourceNodeId] : null;

        public void PushFrame(Frame frame)
        {
            if (FrameCount >= Frames.Length)
                throw new InvalidOperationException("Nesting depth exceeded thread frame capacity (soft-capped at 12 in the editor, TDD §8.4).");
            Frames[FrameCount++] = frame;
        }

        public void PopFrame() => FrameCount--;

        public ref Frame TopFrame() => ref Frames[FrameCount - 1];
    }

    /// <summary>
    /// A copy of everything about one thread that changes as it runs, for Step back. Restoring writes it back into
    /// the same <see cref="VmThread"/> object, so anything still holding that thread keeps a valid reference.
    /// </summary>
    internal sealed class ThreadMoment
    {
        public readonly VmThread Thread;
        private readonly int _pc;
        private readonly Frame[] _frames;
        private readonly int _frameCount;
        private readonly float[] _scratch;
        private readonly ThreadState _state;
        private readonly float _wakeAt;
        private readonly int _generation;
        private readonly int _activePc;
        private readonly int _resumePc;

        public ThreadMoment(VmThread thread)
        {
            Thread = thread;
            _pc = thread.Pc;
            _frames = (Frame[])thread.Frames.Clone();
            _frameCount = thread.FrameCount;
            _scratch = (float[])thread.Scratch.Clone();
            _state = thread.State;
            _wakeAt = thread.WakeAt;
            _generation = thread.Generation;
            _activePc = thread.ActivePc;
            _resumePc = thread.ResumePc;
        }

        public void Restore()
        {
            var thread = Thread;
            thread.Pc = _pc;
            Array.Copy(_frames, thread.Frames, _frames.Length);
            thread.FrameCount = _frameCount;
            Array.Copy(_scratch, thread.Scratch, _scratch.Length);
            thread.State = _state;
            thread.WakeAt = _wakeAt;
            thread.Generation = _generation;
            thread.ActivePc = _activePc;
            thread.ResumePc = _resumePc;
            thread.StepBudget = 0;
            thread.StepParked = false;
        }
    }
}
