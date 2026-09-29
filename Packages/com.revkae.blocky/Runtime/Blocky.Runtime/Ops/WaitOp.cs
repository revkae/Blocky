namespace Blocky.Runtime.Ops
{
    /// <summary><c>control.wait</c> — sets Sleeping + WakeAt (TDD §9). Scratch holds the recorded wake time so re-entry is distinguishable from the first call.</summary>
    [BlockExecutor("control.wait")]
    public sealed class WaitOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var seconds = ctx.GetNumber(0);
            if (seconds <= 0f) return OpResult.Continue;

            if (ctx.Scratch == 0f)
            {
                ctx.Scratch = ctx.Now + seconds;
                ctx.Thread.State = ThreadState.Sleeping;
                ctx.Thread.WakeAt = ctx.Scratch;
                return OpResult.Retry;
            }

            // Scheduler only re-invokes us once Now >= WakeAt.
            ctx.Scratch = 0f;
            return OpResult.Continue;
        }
    }
}
