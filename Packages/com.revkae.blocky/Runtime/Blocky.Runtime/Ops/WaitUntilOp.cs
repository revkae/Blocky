namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.wait_until</c> — holds the script here until the condition becomes true, re-checking once per
    /// frame. <see cref="OpResult.Retry"/> rather than a sleep: there is no wake time to compute, and the yield it
    /// implies is what keeps a waiting script from burning the frame's instruction budget.
    /// </summary>
    [BlockExecutor("control.wait_until")]
    public sealed class WaitUntilOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx) => ctx.GetBool(0) ? OpResult.Continue : OpResult.Retry;
    }
}
