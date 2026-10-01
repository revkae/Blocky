using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>looks.change_color</c> (TDD §9). Recolors every renderer under the object, in every material slot: an
    /// imported model keeps its meshes under a root with no renderer of its own, and recoloring only the root's did
    /// nothing at all there. Materials are per-object copies (ADR-008), so nothing else that shares them changes.
    /// Converges toward the target color using a remaining-fraction lerp each tick
    /// (<c>Lerp(current, target, step / remaining)</c>) instead of remembering a start color — the single float in
    /// <c>Scratch</c> only has room for elapsed time, not a whole start value (see the vault's Runtime notes on why
    /// <see cref="MoveForwardOp"/> uses incremental deltas instead of interpolation) — which also lets each
    /// renderer start from its own color.
    /// </summary>
    [BlockExecutor("looks.change_color")]
    public sealed class ChangeColorOp : IBlockOp
    {
        // One op serves every script and is never re-entered, so these are reused: a timed change runs every frame.
        private readonly List<Renderer> _renderers = new();
        private readonly List<Material> _materials = new();

        public OpResult Execute(ref OpContext ctx)
        {
            BlockyMaterials.RenderersUnder(ctx.Target, _renderers);
            if (_renderers.Count == 0) return OpResult.Continue;
            if (!ColorUtility.TryParseHtmlString(ctx.GetText(0), out var target)) return Done(OpResult.Continue);

            var duration = ctx.GetNumber(1);
            if (duration <= 0f)
            {
                Recolor(target, 1f);
                return Done(OpResult.Continue);
            }

            var remaining = duration - ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, remaining);
            Recolor(target, step / remaining);
            ctx.Scratch += step;

            if (ctx.Scratch >= duration)
            {
                ctx.Scratch = 0f;
                return Done(OpResult.Continue);
            }

            return Done(OpResult.Retry);
        }

        /// <summary>Moves every color a fraction <paramref name="t"/> of the way to <paramref name="target"/> (1 = all the way).</summary>
        private void Recolor(Color target, float t)
        {
            foreach (var renderer in _renderers)
            {
                // A sprite is tinted through its own color: its material is shared by every sprite, and the 2D shaders
                // may have no color property of their own to set.
                if (renderer is SpriteRenderer sprite)
                {
                    sprite.color = Color.Lerp(sprite.color, target, t);
                    continue;
                }

                renderer.GetMaterials(_materials); // this object's own copies, made the first time they are asked for
                foreach (var material in _materials)
                    if (BlockyMaterials.TryGetColor(material, out var current))
                        BlockyMaterials.SetColor(material, Color.Lerp(current, target, t));
            }
        }

        /// <summary>The lists hold nothing between calls: an object destroyed later must not be kept alive from here.</summary>
        private OpResult Done(OpResult result)
        {
            _renderers.Clear();
            _materials.Clear();
            return result;
        }
    }
}
