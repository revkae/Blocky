using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary><c>motion.set_rotation</c> — instant, no duration param (TDD §9). Choice 0 = world, 1 = local.</summary>
    [BlockExecutor("motion.set_rotation")]
    public sealed class SetRotationOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var euler = new Vector3(ctx.GetNumber(0), ctx.GetNumber(1), ctx.GetNumber(2));
            var isWorldSpace = ctx.Params[3].ChoiceIndex == 0;

            if (isWorldSpace) ctx.Target.transform.eulerAngles = euler;
            else ctx.Target.transform.localEulerAngles = euler;

            return OpResult.Continue;
        }
    }
}
