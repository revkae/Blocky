using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary><c>motion.move_forward</c> — <c>duration = 0</c> is instant; otherwise Retry until elapsed, using thread scratch as elapsed time (TDD §9).</summary>
    [BlockExecutor("motion.move_forward")]
    public sealed class MoveForwardOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var distance = ctx.Params[0].Number;
            var duration = ctx.Params[1].Number;
            var transform = ctx.Target.transform;

            if (duration <= 0f)
            {
                transform.Translate(transform.forward * distance, Space.World);
                return OpResult.Continue;
            }

            var elapsed = ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, duration - elapsed);
            transform.Translate(transform.forward * (distance * step / duration), Space.World);
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
