using Blocky.Compiler;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>custom.run</c> — "run [name] a () b () c ()": runs this object's <c>define [name]</c> script with those three
    /// values, then carries on with the next block (ADR-029). The values are worked out before the call, in the
    /// caller's own context, so <c>run [count] a ((input a) - (1))</c> inside its own definition counts down.
    /// A name no definition on this object has does nothing, and so does going deeper than
    /// <see cref="VmThread.MaxNestingDepth"/>. A block that runs itself waits one frame first, as in Scratch: a
    /// recursive drawing unfolds on screen, and one that never stops slows down instead of freezing the game.
    /// </summary>
    [BlockExecutor(CustomBlocks.RunType)]
    public sealed class RunCustomBlockOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var thread = ctx.Thread;
            if (!thread.Program.TryFindProcedure(ctx.GetText(0), out var entryPc, out var exitPc)) return OpResult.Continue;
            if (thread.FrameCount >= VmThread.MaxNestingDepth) return OpResult.Continue;

            var recursive = thread.IsInsideDefinition(exitPc);
            thread.EnterCall(ctx.Pc, exitPc, ctx.GetValue(1), ctx.GetValue(2), ctx.GetValue(3));
            ctx.NextPc = entryPc;

            if (recursive)
            {
                thread.State = ThreadState.YieldedFrame;
                thread.MarkFramesYielded(); // this wait counts as the lap's yield, so a loop around the call adds no second one
            }
            return OpResult.Jump;
        }
    }

    /// <summary>
    /// <c>custom.input_a</c>, <c>_b</c>, <c>_c</c> — a value the <c>run</c> block handed the definition this block is
    /// in; with recursion, the innermost run's. Outside a definition there is none, and it reads as empty.
    /// </summary>
    public abstract class CustomBlockInputValue : IValueOp
    {
        private readonly int _index;

        protected CustomBlockInputValue(int index) => _index = index;

        public BlockValue Evaluate(ref SlotContext ctx) => ctx.Thread.Input(_index);
    }

    [BlockExecutor("custom.input_a")]
    public sealed class CustomBlockInputA : CustomBlockInputValue
    {
        public CustomBlockInputA() : base(0) { }
    }

    [BlockExecutor("custom.input_b")]
    public sealed class CustomBlockInputB : CustomBlockInputValue
    {
        public CustomBlockInputB() : base(1) { }
    }

    [BlockExecutor("custom.input_c")]
    public sealed class CustomBlockInputC : CustomBlockInputValue
    {
        public CustomBlockInputC() : base(2) { }
    }
}
