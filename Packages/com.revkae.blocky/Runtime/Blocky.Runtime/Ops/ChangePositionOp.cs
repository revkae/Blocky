using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>motion.change_position</c> — Scratch's "change x by", in three axes at once. <c>duration = 0</c> is
    /// instant; otherwise the offset is applied as a per-tick increment (the relative-delta technique — no start
    /// position has to be remembered, which matters because a thread has exactly one float of scratch per block).
    /// <c>space</c> is a Choice param: choice 0 = world axes, 1 = the object's own axes.
    /// </summary>
    [BlockExecutor("motion.change_position")]
    public sealed class ChangePositionOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var offset = new Vector3(ctx.GetNumber(0), ctx.GetNumber(1), ctx.GetNumber(2));
            var duration = ctx.GetNumber(3);
            var space = ctx.Params[4].ChoiceIndex == 0 ? Space.World : Space.Self;
            var transform = ctx.Target.transform;

            if (duration <= 0f)
            {
                transform.Translate(offset, space);
                return OpResult.Continue;
            }

            var elapsed = ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, duration - elapsed);
            transform.Translate(offset * (step / duration), space);
            elapsed += step;

            if (elapsed >= duration)
            {
                ctx.Scratch = 0f;
                return OpResult.Continue;
            }

            ctx.Scratch = elapsed;
            return OpResult.Retry;
        }
    }
}
