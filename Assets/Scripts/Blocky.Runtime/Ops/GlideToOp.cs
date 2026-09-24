using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>motion.glide_to</c> — Scratch's "glide to x y z". The target is absolute, so this uses the
    /// remaining-fraction convergence <c>ChangeColorOp</c> documents: <c>Lerp(current, target, step / remaining)</c>
    /// lands on the target as the remaining time reaches zero, without ever storing where the glide began.
    /// </summary>
    [BlockExecutor("motion.glide_to")]
    public sealed class GlideToOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var target = new Vector3(ctx.GetNumber(0), ctx.GetNumber(1), ctx.GetNumber(2));
            var duration = ctx.GetNumber(3);
            var transform = ctx.Target.transform;

            if (duration <= 0f)
            {
                transform.position = target;
                return OpResult.Continue;
            }

            var remaining = duration - ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, remaining);
            transform.position = Vector3.Lerp(transform.position, target, step / remaining);
            ctx.Scratch += step;

            if (ctx.Scratch >= duration)
            {
                transform.position = target; // float error accumulates over a long glide; the last tick states the answer
                ctx.Scratch = 0f;
                return OpResult.Continue;
            }

            return OpResult.Retry;
        }
    }
}
