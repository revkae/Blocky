using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary><c>motion.set_position</c> — instant (TDD §9). <c>space</c> is a Choice param: choice 0 = world, 1 = local.</summary>
    [BlockExecutor("motion.set_position")]
    public sealed class SetPositionOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var position = new Vector3(ctx.Params[0].Number, ctx.Params[1].Number, ctx.Params[2].Number);
            var isWorldSpace = ctx.Params[3].ChoiceIndex == 0;

            if (isWorldSpace) ctx.Target.transform.position = position;
            else ctx.Target.transform.localPosition = position;

            return OpResult.Continue;
        }
    }
}
