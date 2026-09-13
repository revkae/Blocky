namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.repeat_until</c> — condition evaluated before each iteration (TDD §9). The condition param is
    /// a Bool literal in v1 (becomes a Reporter slot with no schema change once expression blocks land) — this
    /// op already re-reads it fresh on every lap, so nothing here changes when that lands.
    /// </summary>
    [BlockExecutor("control.repeat_until")]
    public sealed class RepeatUntilOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var thread = ctx.Thread;

            if (thread.FrameCount > 0 && thread.TopFrame().OwnerPc == ctx.Pc)
                thread.PopFrame(); // previous lap's frame; we'll push a fresh one below if we loop again

            if (ctx.Params[0].Boolean)
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
