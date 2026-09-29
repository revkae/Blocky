namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>event.level_complete</c> — "level complete": tells the game the puzzle is solved, then carries straight on.
    /// Game code hears it as <see cref="BlockyEvents.LevelCompleted"/> (with this object), and every
    /// <c>when level complete</c> script starts — on the next tick, like a broadcast's listeners — so a teacher can
    /// build a whole level from blocks: the goal's "when collided → level complete", the player's "when level complete
    /// → say [You did it!]". What completing means beyond that (a new level, a score) is the game's to decide.
    /// </summary>
    [BlockExecutor("event.level_complete")]
    public sealed class LevelCompleteOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Triggers.RaiseLevelCompleted(ctx.Target);
            return OpResult.Continue;
        }
    }
}
