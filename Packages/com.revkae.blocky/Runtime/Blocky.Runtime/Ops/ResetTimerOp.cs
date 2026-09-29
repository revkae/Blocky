namespace Blocky.Runtime.Ops
{
    /// <summary><c>sensing.reset_timer</c> — puts the shared timer back to zero, for <c>timer &gt; n?</c> to measure from.</summary>
    [BlockExecutor("sensing.reset_timer")]
    public sealed class ResetTimerOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            ctx.Scheduler?.ResetTimer();
            return OpResult.Continue;
        }
    }
}
