namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.repeat_until</c> — condition evaluated before each iteration (TDD §9). The condition slot is
    /// re-evaluated fresh on every lap through <see cref="OpContext.GetBool"/>, so a live condition such as
    /// <c>mouse down?</c> ends the loop the lap after it becomes true.
    /// </summary>
    [BlockExecutor("control.repeat_until")]
    public sealed class RepeatUntilOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var thread = ctx.Thread;

            if (thread.FrameCount > 0 && thread.TopFrame().OwnerPc == ctx.Pc)
                thread.PopFrame(); // previous lap's frame; we'll push a fresh one below if we loop again

            if (ctx.GetBool(0))
            {
                ctx.NextPc = ctx.Instruction.JumpAExit;
                return OpResult.Jump;
            }

            thread.PushFrame(new Frame(ctx.Pc, ctx.Instruction.JumpAExit, 0, isLoop: true));
            ctx.NextPc = ctx.Instruction.JumpA;
            return OpResult.Jump;
        }
    }
}
