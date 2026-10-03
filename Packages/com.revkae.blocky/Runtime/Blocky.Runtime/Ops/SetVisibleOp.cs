using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>looks.set_visible</c> — shows or hides the object: every renderer under it (TDD §9). An imported model keeps
    /// its meshes under a root with no renderer of its own, and switching only the root's did nothing at all there —
    /// the same reason "change color" recolors them all (ADR-043). A speech bubble is left alone: a hidden object can
    /// still say where it is.
    /// </summary>
    [BlockExecutor("looks.set_visible")]
    public sealed class SetVisibleOp : IBlockOp
    {
        // One op serves every script and is never re-entered, so the list is reused rather than allocated per run.
        private readonly List<Renderer> _renderers = new();

        public OpResult Execute(ref OpContext ctx)
        {
            var visible = ctx.GetBool(0);
            BlockyMaterials.RenderersUnder(ctx.Target, _renderers);
            foreach (var renderer in _renderers) renderer.enabled = visible;

            _renderers.Clear(); // hold nothing between runs: an object destroyed later must not be kept alive from here
            return OpResult.Continue;
        }
    }
}
