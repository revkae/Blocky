namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.if_else</c> — two branches laid out back-to-back in the flat code array, so falling through
    /// the taken (true) branch would run straight into the false branch unless intercepted. On the true path we
    /// push a one-shot, non-looping frame (TDD §9) whose only job is to redirect that fall-through to
    /// <c>JumpBExit</c>, skipping the false branch entirely. The false path needs no frame: <c>JumpB</c>'s
    /// branch naturally falls through to <c>JumpBExit</c>, which is already the correct continuation.
    /// </summary>
    [BlockExecutor("control.if_else")]
    public sealed class IfElseOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var thread = ctx.Thread;

            if (thread.FrameCount > 0 && thread.TopFrame().OwnerPc == ctx.Pc)
            {
                thread.PopFrame();
                ctx.NextPc = ctx.Instruction.JumpBExit; // true branch just finished; skip the false branch
                return OpResult.Jump;
            }

            if (ctx.Params[0].Boolean)
            {
                thread.PushFrame(new Frame(ctx.Pc, ctx.Instruction.JumpAExit, 0, isLoop: false));
                ctx.NextPc = ctx.Instruction.JumpA;
            }
            else
            {
                ctx.NextPc = ctx.Instruction.JumpB;
            }

            return OpResult.Jump;
        }
    }
}
