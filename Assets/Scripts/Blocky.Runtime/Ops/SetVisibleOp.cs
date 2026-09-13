namespace Blocky.Runtime.Ops
{
    /// <summary><c>looks.set_visible</c> — toggles the target's renderer, if it has one (TDD §9).</summary>
    [BlockExecutor("looks.set_visible")]
    public sealed class SetVisibleOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var renderer = ctx.Target.GetComponent<UnityEngine.Renderer>();
            if (renderer != null) renderer.enabled = ctx.Params[0].Boolean;
            return OpResult.Continue;
        }
    }
}
