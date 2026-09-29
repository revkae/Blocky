using System;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// The comparison operators, which are conditions (hexagons) whose inputs are ovals. Scratch compares two
    /// things that both look like numbers as numbers, and anything else as words, case-insensitively — so
    /// <c>"10" = "10.0"</c> is true, and <c>"apple" = "APPLE"</c> is true as well. <see cref="Compare"/> is that
    /// one rule, shared by all three so they can never disagree.
    /// </summary>
    internal static class ValueComparison
    {
        public static int Compare(in BlockValue a, in BlockValue b) =>
            a.IsNumericWith(b)
                ? a.AsNumber().CompareTo(b.AsNumber())
                : string.Compare(a.AsText(), b.AsText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>operator.lt</c> — <c>(a) &lt; (b)</c>.</summary>
    [BlockExecutor("operator.lt")]
    public sealed class LessThanCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) => ValueComparison.Compare(ctx.GetValue(0), ctx.GetValue(1)) < 0;
    }

    /// <summary><c>operator.eq</c> — <c>(a) = (b)</c>.</summary>
    [BlockExecutor("operator.eq")]
    public sealed class EqualsCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) => ValueComparison.Compare(ctx.GetValue(0), ctx.GetValue(1)) == 0;
    }

    /// <summary><c>operator.gt</c> — <c>(a) &gt; (b)</c>.</summary>
    [BlockExecutor("operator.gt")]
    public sealed class GreaterThanCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) => ValueComparison.Compare(ctx.GetValue(0), ctx.GetValue(1)) > 0;
    }

    /// <summary><c>operator.contains</c> — does (text) contain (thing)? Case-insensitive, like Scratch's.</summary>
    [BlockExecutor("operator.contains")]
    public sealed class ContainsCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx)
        {
            var needle = ctx.GetText(1);
            return needle.Length == 0 || ctx.GetText(0).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
