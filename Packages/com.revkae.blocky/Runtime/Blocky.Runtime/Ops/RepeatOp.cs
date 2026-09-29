namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.repeat</c> — one branch, loop counter in the thread's frame. On re-entry (frame already owned
    /// by this pc) decrements and either loops again or pops and falls through to <c>JumpAExit</c> (TDD §9).
    /// </summary>
    [BlockExecutor("control.repeat")]
    public sealed class RepeatOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var thread = ctx.Thread;

            if (thread.FrameCount > 0 && thread.TopFrame().OwnerPc == ctx.Pc)
            {
                ref var frame = ref thread.TopFrame();
                frame.Counter--;
                if (frame.Counter > 0)
                {
                    ctx.NextPc = ctx.Instruction.JumpA;
                    return OpResult.Jump;
                }

                thread.PopFrame();
                ctx.NextPc = ctx.Instruction.JumpAExit;
                return OpResult.Jump;
            }

            var times = (int)ctx.GetNumber(0);
            if (times <= 0)
            {
                ctx.NextPc = ctx.Instruction.JumpAExit;
                return OpResult.Jump;
            }

            thread.PushFrame(new Frame(ctx.Pc, ctx.Instruction.JumpAExit, times, isLoop: true));
            ctx.NextPc = ctx.Instruction.JumpA;
            return OpResult.Jump;
        }
    }
}
