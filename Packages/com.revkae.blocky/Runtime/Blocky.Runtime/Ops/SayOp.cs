using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>looks.say</c> — the bubble over an object's head. One block covers all four of Scratch's:
    /// <c>seconds = 0</c> leaves the bubble up until something changes it (Scratch's <c>say</c>), any other value
    /// shows it for that long and then clears it (<c>say for … seconds</c>), and the style dropdown is the
    /// difference between saying and thinking. Saying nothing clears the bubble, exactly as an empty
    /// <c>say</c> does in Scratch.
    ///
    /// The timed form holds the script here, so <c>say "ready" for 1 → say "go!" for 1</c> reads in order.
    /// </summary>
    [BlockExecutor("looks.say")]
    public sealed class SayOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var seconds = ctx.GetNumber(1);
            var think = ctx.Params[2].ChoiceIndex == 1;

            if (seconds <= 0f)
            {
                BlockySpeechBubble.Show(ctx.Target, ctx.GetText(0), think);
                return OpResult.Continue;
            }

            if (ctx.Scratch == 0f)
            {
                BlockySpeechBubble.Show(ctx.Target, ctx.GetText(0), think);
                ctx.Scratch = ctx.Now + seconds;
                ctx.Thread.State = ThreadState.Sleeping;
                ctx.Thread.WakeAt = ctx.Scratch;
                return OpResult.Retry;
            }

            // The scheduler only brings us back once the wake time has passed.
            BlockySpeechBubble.Hide(ctx.Target);
            ctx.Scratch = 0f;
            return OpResult.Continue;
        }
    }
}
