using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// Which copy of a variable a block means. The choice is read by index, so the order in the assets
    /// (everyone, this object) is part of the contract — and it is the same order on all three variable blocks.
    /// </summary>
    internal static class VariableScope
    {
        /// <summary>The object that owns this copy of the variable, or null for the one everything shares.</summary>
        public static GameObject Owner(int choiceIndex, GameObject target) => choiceIndex == 1 ? target : null;
    }

    /// <summary>
    /// <c>variables.set</c> — "set [score] to (0)". The value keeps what it is: a number stays a number, so
    /// <c>change by</c> and <c>&gt;</c> keep working on it afterwards.
    /// </summary>
    [BlockExecutor("variables.set")]
    public sealed class SetVariableOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Variables.Set(ctx.GetText(0), ctx.GetValue(1), VariableScope.Owner(ctx.Params[2].ChoiceIndex, ctx.Target));
            return OpResult.Continue;
        }
    }

    /// <summary>
    /// <c>variables.change</c> — "change [score] by (1)". Whatever is in the variable is read as a number first,
    /// so changing one that was never set counts from 0 instead of failing.
    /// </summary>
    [BlockExecutor("variables.change")]
    public sealed class ChangeVariableOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Variables.Change(ctx.GetText(0), ctx.GetNumber(1), VariableScope.Owner(ctx.Params[2].ChoiceIndex, ctx.Target));
            return OpResult.Continue;
        }
    }

    /// <summary><c>variables.get</c> — the variable as a value, for any input. Never set reads as empty: 0 as a number, "" as text.</summary>
    [BlockExecutor("variables.get")]
    public sealed class GetVariableValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) =>
            BlockyRuntime.Variables.Get(ctx.GetText(0), VariableScope.Owner(ctx.Params[1].ChoiceIndex, ctx.Target));
    }
}
