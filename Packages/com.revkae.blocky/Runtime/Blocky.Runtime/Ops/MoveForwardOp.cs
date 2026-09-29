using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>motion.move_forward</c> — <c>duration = 0</c> is instant; otherwise Retry until elapsed, using thread scratch
    /// as elapsed time (TDD §9). Forward is the object's Z axis in 3D and its X axis on a 2D stage (<see cref="BlockyPlane"/>).
    /// </summary>
    [BlockExecutor("motion.move_forward")]
    public sealed class MoveForwardOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var distance = ctx.GetNumber(0);
            var duration = ctx.GetNumber(1);
            var transform = ctx.Target.transform;
            var forward = BlockyPlane.Forward(transform, BlockyPlane.IsFlat(ctx.Target));

            if (duration <= 0f)
            {
                transform.Translate(forward * distance, Space.World);
                return OpResult.Continue;
            }

            var elapsed = ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, duration - elapsed);
            transform.Translate(forward * (distance * step / duration), Space.World);
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
