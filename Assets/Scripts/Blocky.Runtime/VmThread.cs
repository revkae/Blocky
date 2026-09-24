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
        /// <summary>
        /// How deep a script can go: loops inside loops, and custom blocks running custom blocks (recursion), counted
        /// together. Past it a <c>run</c> block does nothing, which ends a runaway recursion instead of the game.
        /// The frame array starts small and grows only when a script actually goes deep.
        /// </summary>
        public const int MaxNestingDepth = 256;

        private const int InitialFrames = 16;

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

        /// <summary>The frame stack, innermost last; only the first <see cref="FrameCount"/> are live. Replaced by a bigger array when a script nests deeper.</summary>
        public Frame[] Frames = new Frame[InitialFrames];
        public int FrameCount;

        /// <summary>
        /// Where the code this thread is running right now ends: <see cref="ExitPc"/>, or inside a custom block, the
        /// end of that block's definition — a definition's code sits outside the calling script's range, so the
        /// script's own exit can't be the bound there (ADR-029).
        /// </summary>
        public int EndPc;

        /// <summary>How many custom blocks this thread is inside right now (recursion counts each level).</summary>
        public int CallDepth;

        // The values each call was handed (a, b, c), innermost call last — CustomBlocks.InputCount per call.
        // Made on the first call, so a script that never runs a custom block allocates nothing for it.
        private BlockValue[] _inputs;
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
            EndPc = ExitPc;
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
            if (FrameCount >= MaxNestingDepth)
                throw new InvalidOperationException($"Nesting deeper than {MaxNestingDepth} (loops and custom blocks together).");
            if (FrameCount == Frames.Length) Array.Resize(ref Frames, Math.Min(Frames.Length * 2, MaxNestingDepth));
            Frames[FrameCount++] = frame;
        }

        public void PopFrame() => FrameCount--;

        public ref Frame TopFrame() => ref Frames[FrameCount - 1];

        /// <summary>Marks every live frame as having yielded this lap, so the loop-yield rule adds no second yield.</summary>
        public void MarkFramesYielded()
        {
            for (var i = 0; i < FrameCount; i++) Frames[i].YieldedThisLap = true;
        }

        // ---- custom blocks (ADR-029) -------------------------------------------------------------------

        /// <summary>
        /// Starts a custom block: the block at <paramref name="ownerPc"/> hands <paramref name="a"/>, <paramref name="b"/>
        /// and <paramref name="c"/> to the definition that ends at <paramref name="exitPc"/>. The caller jumps to the
        /// definition's first block; when the thread reaches <paramref name="exitPc"/>, <see cref="ReturnFromCall"/>
        /// brings it back.
        /// </summary>
        public void EnterCall(int ownerPc, int exitPc, BlockValue a, BlockValue b, BlockValue c)
        {
            PushFrame(Frame.Call(ownerPc, exitPc, EndPc));

            var start = CallDepth * CustomBlocks.InputCount;
            var needed = start + CustomBlocks.InputCount;
            if (_inputs == null) _inputs = new BlockValue[Math.Max(needed, 4 * CustomBlocks.InputCount)];
            else if (_inputs.Length < needed) Array.Resize(ref _inputs, Math.Max(needed, _inputs.Length * 2));

            _inputs[start] = a;
            _inputs[start + 1] = b;
            _inputs[start + 2] = c;
            CallDepth++;
            EndPc = exitPc;
        }

        /// <summary>The custom block on top has finished: carry on with the block after the one that ran it.</summary>
        public void ReturnFromCall()
        {
            ref var frame = ref TopFrame();
            Pc = frame.OwnerPc + 1;
            EndPc = frame.CallerEndPc;
            CallDepth--;
            PopFrame();
        }

        /// <summary>Input <paramref name="index"/> (0 = a) of the innermost custom block this thread is in — empty outside one.</summary>
        public BlockValue Input(int index)
        {
            if (CallDepth == 0 || index < 0 || index >= CustomBlocks.InputCount) return BlockValue.Empty;
            return _inputs[(CallDepth - 1) * CustomBlocks.InputCount + index];
        }

        /// <summary>True when the definition ending at <paramref name="exitPc"/> is already running on this thread — a recursive call.</summary>
        public bool IsInsideDefinition(int exitPc)
        {
            for (var i = 0; i < FrameCount; i++)
                if (Frames[i].IsCall && Frames[i].ExitPc == exitPc) return true;
            return false;
        }

        internal BlockValue[] Inputs => _inputs;

        internal void RestoreCalls(int callDepth, int endPc, BlockValue[] inputs)
        {
            CallDepth = callDepth;
            EndPc = endPc;
            if (inputs == null || inputs.Length == 0) return;
            if (_inputs == null || _inputs.Length < inputs.Length) _inputs = new BlockValue[Math.Max(inputs.Length, 4 * CustomBlocks.InputCount)];
            Array.Copy(inputs, _inputs, inputs.Length);
        }
    }

    /// <summary>
    /// A copy of everything about one thread that changes as it runs, for Step back. Restoring writes it back into
    /// the same <see cref="VmThread"/> object, so anything still holding that thread keeps a valid reference.
    /// </summary>
    internal sealed class ThreadMoment
    {
        public readonly VmThread Thread;
        private readonly int _pc;
        private readonly Frame[] _frames; // the live ones only
        private readonly int _endPc;
        private readonly int _callDepth;
        private readonly BlockValue[] _inputs; // the live calls' only
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
            _frames = new Frame[thread.FrameCount];
            Array.Copy(thread.Frames, _frames, thread.FrameCount);
            _endPc = thread.EndPc;
            _callDepth = thread.CallDepth;
            var inputCount = thread.CallDepth * CustomBlocks.InputCount;
            _inputs = inputCount == 0 ? null : new BlockValue[inputCount];
            if (_inputs != null) Array.Copy(thread.Inputs, _inputs, inputCount);
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
            if (thread.Frames.Length < _frames.Length) thread.Frames = new Frame[_frames.Length];
            Array.Copy(_frames, thread.Frames, _frames.Length);
            thread.FrameCount = _frames.Length;
            thread.RestoreCalls(_callDepth, _endPc, _inputs);
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
