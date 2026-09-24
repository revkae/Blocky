namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.stop</c> — Scratch's stop block. The choice decides how far it reaches: this script only, every
    /// other script on this object, or everything in the scene. Ending a thread is just
    /// <see cref="ThreadState.Done"/>; the scheduler drops it at the end of the tick.
    /// </summary>
    [BlockExecutor("control.stop")]
    public sealed class StopOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            switch (ctx.Params[0].ChoiceIndex)
            {
                case 1: // all
                    ctx.Scheduler?.StopAllThreads();
                    ctx.Thread.State = ThreadState.Done;
                    break;
                case 2: // other scripts on this object — this one carries on
                    ctx.Scheduler?.StopOtherThreadsOn(ctx.Target, ctx.Thread);
                    return OpResult.Continue;
                default: // this script
                    ctx.Thread.State = ThreadState.Done;
                    break;
            }

            return OpResult.Continue;
        }
    }
}
