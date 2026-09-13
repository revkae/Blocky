using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary><c>looks.set_scale</c> (TDD §9). Same remaining-fraction convergence as <see cref="ChangeColorOp"/> — no separate start-scale memory needed.</summary>
    [BlockExecutor("looks.set_scale")]
    public sealed class SetScaleOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var target = new Vector3(ctx.Params[0].Number, ctx.Params[1].Number, ctx.Params[2].Number);
            var duration = ctx.Params[3].Number;
            var transform = ctx.Target.transform;

            if (duration <= 0f)
            {
                transform.localScale = target;
                return OpResult.Continue;
            }

            var remaining = duration - ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, remaining);
            transform.localScale = Vector3.Lerp(transform.localScale, target, step / remaining);
            ctx.Scratch += step;

            if (ctx.Scratch >= duration)
            {
                ctx.Scratch = 0f;
                return OpResult.Continue;
            }

            return OpResult.Retry;
        }
    }
}
