namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>event.broadcast</c> — sends a named message to every script in the scene and carries straight on, exactly
    /// like Scratch's <c>broadcast</c>. Receivers are started through the scheduler, so they begin on the next tick
    /// and never run inside this one (TDD §6.4) — the sender cannot be re-entered by its own message.
    /// </summary>
    [BlockExecutor("event.broadcast")]
    public sealed class BroadcastOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Triggers.Broadcast(ctx.GetText(0));
            return OpResult.Continue;
        }
    }
}
