using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary><c>motion.turn_direction</c> — relative turn, same instant/Retry-until-elapsed shape as <see cref="MoveForwardOp"/> (TDD §9). Choice 0 = left, 1 = right.</summary>
    [BlockExecutor("motion.turn_direction")]
    public sealed class TurnDirectionOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var sign = ctx.Params[0].ChoiceIndex == 0 ? 1f : -1f;
            var degrees = ctx.GetNumber(1);
            var duration = ctx.GetNumber(2);
            var transform = ctx.Target.transform;

            if (duration <= 0f)
            {
                transform.Rotate(Vector3.up, sign * degrees, Space.World);
                return OpResult.Continue;
            }

            var elapsed = ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, duration - elapsed);
            transform.Rotate(Vector3.up, sign * degrees * step / duration, Space.World);
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
