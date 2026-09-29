namespace Blocky.Runtime.Ops
{
    // Scratch's lists (ADR-028). Every list block names its list in a text input and ends with the same
    // everyone / this object dropdown as the variable blocks, read the same way (VariableScope). Items are
    // BlockValues, so a number added to a list is still a number when it comes back out. The input order follows
    // Scratch's wording: "add (thing) to [list]", "replace item (1) of [list] with (thing)".

    /// <summary><c>lists.add</c> — "add (thing) to [list]".</summary>
    [BlockExecutor("lists.add")]
    public sealed class AddToListOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Variables.Add(ctx.GetText(1), ctx.GetValue(0), VariableScope.Owner(ctx.Params[2].ChoiceIndex, ctx.Target));
            return OpResult.Continue;
        }
    }

    /// <summary><c>lists.delete</c> — "delete (1) of [list]". The items after it move up one.</summary>
    [BlockExecutor("lists.delete")]
    public sealed class DeleteFromListOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Variables.DeleteAt(ctx.GetText(1), ctx.GetNumber(0), VariableScope.Owner(ctx.Params[2].ChoiceIndex, ctx.Target));
            return OpResult.Continue;
        }
    }

    /// <summary><c>lists.delete_all</c> — "delete all of [list]". The list stays, empty.</summary>
    [BlockExecutor("lists.delete_all")]
    public sealed class DeleteAllOfListOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Variables.DeleteAll(ctx.GetText(0), VariableScope.Owner(ctx.Params[1].ChoiceIndex, ctx.Target));
            return OpResult.Continue;
        }
    }

    /// <summary><c>lists.insert</c> — "insert (thing) at (1) of [list]". One past the end adds to the end.</summary>
    [BlockExecutor("lists.insert")]
    public sealed class InsertIntoListOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Variables.Insert(ctx.GetText(2), ctx.GetNumber(1), ctx.GetValue(0), VariableScope.Owner(ctx.Params[3].ChoiceIndex, ctx.Target));
            return OpResult.Continue;
        }
    }

    /// <summary><c>lists.replace</c> — "replace item (1) of [list] with (thing)".</summary>
    [BlockExecutor("lists.replace")]
    public sealed class ReplaceInListOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Variables.Replace(ctx.GetText(1), ctx.GetNumber(0), ctx.GetValue(2), VariableScope.Owner(ctx.Params[3].ChoiceIndex, ctx.Target));
            return OpResult.Continue;
        }
    }

    /// <summary><c>lists.item</c> — "item (1) of [list]". A position the list doesn't have reads as empty.</summary>
    [BlockExecutor("lists.item")]
    public sealed class ItemOfListValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) =>
            BlockyRuntime.Variables.Item(ctx.GetText(1), ctx.GetNumber(0), VariableScope.Owner(ctx.Params[2].ChoiceIndex, ctx.Target));
    }

    /// <summary><c>lists.index_of</c> — "item # of (thing) in [list]": where it first appears, from 1, or 0 when it isn't there.</summary>
    [BlockExecutor("lists.index_of")]
    public sealed class PositionInListValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) =>
            BlockValue.Number(BlockyRuntime.Variables.PositionOf(ctx.GetText(1), ctx.GetValue(0), VariableScope.Owner(ctx.Params[2].ChoiceIndex, ctx.Target)));
    }

    /// <summary><c>lists.length</c> — "length of [list]". A list nothing has used yet is 0 long.</summary>
    [BlockExecutor("lists.length")]
    public sealed class LengthOfListValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) =>
            BlockValue.Number(BlockyRuntime.Variables.Length(ctx.GetText(0), VariableScope.Owner(ctx.Params[1].ChoiceIndex, ctx.Target)));
    }

    /// <summary><c>lists.contains</c> — "[list] contains (thing)?", matching the way <c>=</c> does, so 10 finds "10.0".</summary>
    [BlockExecutor("lists.contains")]
    public sealed class ListContainsCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) =>
            BlockyRuntime.Variables.Contains(ctx.GetText(0), ctx.GetValue(1), VariableScope.Owner(ctx.Params[2].ChoiceIndex, ctx.Target));
    }
}
