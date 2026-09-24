using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>motion.turn_direction</c> — relative turn, same instant/Retry-until-elapsed shape as <see cref="MoveForwardOp"/>
    /// (TDD §9). Choice 0 = left, 1 = right, from the object's own point of view: in 3D it turns around up, where a
    /// positive angle turns right (facing +Z becomes facing +X); on a 2D stage around Z, where positive is anticlockwise
    /// on screen — a left turn. (Until 2026-09-24 "left" turned right in 3D.)
    /// </summary>
    [BlockExecutor("motion.turn_direction")]
    public sealed class TurnDirectionOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var left = ctx.Params[0].ChoiceIndex == 0;
            var flat = BlockyPlane.IsFlat(ctx.Target);
            var sign = left == flat ? 1f : -1f; // 2D: left is +Z (anticlockwise); 3D: left is -Y
            var axis = BlockyPlane.TurnAxis(flat);
            var degrees = ctx.GetNumber(1);
            var duration = ctx.GetNumber(2);
            var transform = ctx.Target.transform;

            if (duration <= 0f)
            {
                transform.Rotate(axis, sign * degrees, Space.World);
                return OpResult.Continue;
            }

            var elapsed = ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, duration - elapsed);
            transform.Rotate(axis, sign * degrees * step / duration, Space.World);
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
