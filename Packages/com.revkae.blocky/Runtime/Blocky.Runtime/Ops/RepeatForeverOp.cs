namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.repeat_forever</c> — same yield rule as any other loop; cannot fall through (TDD §9). An empty
    /// body needs no special-casing: JumpA == JumpAExit already, so the wrap check fires immediately on
    /// re-entry, forcing one yield per tick forever rather than spinning or crashing.
    /// </summary>
    [BlockExecutor("control.repeat_forever")]
    public sealed class RepeatForeverOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var thread = ctx.Thread;

            if (thread.FrameCount > 0 && thread.TopFrame().OwnerPc == ctx.Pc)
            {
                ctx.NextPc = ctx.Instruction.JumpA;
                return OpResult.Jump;
            }

            thread.PushFrame(new Frame(ctx.Pc, ctx.Instruction.JumpAExit, 0, isLoop: true));
            ctx.NextPc = ctx.Instruction.JumpA;
            return OpResult.Jump;
        }
    }
}
