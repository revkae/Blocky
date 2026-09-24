using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>looks.change_color</c> (TDD §9). Converges toward the target color using a remaining-fraction lerp
    /// each tick (<c>Lerp(current, target, step / remaining)</c>) instead of remembering a start color — the
    /// single float in <c>Scratch</c> only has room for elapsed time, not a whole start value (see the vault's
    /// Runtime notes on why <see cref="MoveForwardOp"/> uses incremental deltas instead of interpolation).
    /// </summary>
    [BlockExecutor("looks.change_color")]
    public sealed class ChangeColorOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var renderer = ctx.Target.GetComponent<Renderer>();
            if (renderer == null) return OpResult.Continue;
            if (!ColorUtility.TryParseHtmlString(ctx.GetText(0), out var target)) return OpResult.Continue;

            var duration = ctx.GetNumber(1);
            if (duration <= 0f)
            {
                renderer.material.color = target;
                return OpResult.Continue;
            }

            var remaining = duration - ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, remaining);
            renderer.material.color = Color.Lerp(renderer.material.color, target, step / remaining);
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
