namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.if</c> — one branch, no loop-back needed at all: taking the branch means jumping to
    /// <c>JumpA</c> and letting it fall through naturally to <c>JumpAExit</c> when done; skipping it means
    /// jumping straight to <c>JumpAExit</c>. No frame required (TDD §9).
    /// </summary>
    [BlockExecutor("control.if")]
    public sealed class IfOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            ctx.NextPc = ctx.Params[0].Boolean ? ctx.Instruction.JumpA : ctx.Instruction.JumpAExit;
            return OpResult.Jump;
        }
    }
}
