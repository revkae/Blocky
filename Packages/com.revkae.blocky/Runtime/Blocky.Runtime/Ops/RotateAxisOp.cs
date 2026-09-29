using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary><c>motion.rotate_axis</c> — relative rotation about a local axis (TDD §9). Choice 0/1/2 = x/y/z.</summary>
    [BlockExecutor("motion.rotate_axis")]
    public sealed class RotateAxisOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var axis = ctx.Params[0].ChoiceIndex switch
            {
                0 => Vector3.right,
                1 => Vector3.up,
                _ => Vector3.forward
            };
            var degrees = ctx.GetNumber(1);
            var duration = ctx.GetNumber(2);
            var transform = ctx.Target.transform;

            if (duration <= 0f)
            {
                transform.Rotate(axis, degrees, Space.Self);
                return OpResult.Continue;
            }

            var elapsed = ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, duration - elapsed);
            transform.Rotate(axis, degrees * step / duration, Space.Self);
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
