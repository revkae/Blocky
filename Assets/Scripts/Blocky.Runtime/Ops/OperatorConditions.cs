namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>operator.and</c> — true when both slots are true. The slots are condition slots themselves, so an
    /// <c>and</c> can hold another <c>and</c>: <see cref="SlotContext.GetBool"/> evaluates whatever is in
    /// them, however deep, and an empty slot reads as false (Scratch's empty hexagon).
    /// </summary>
    [BlockExecutor("operator.and")]
    public sealed class AndCondition : IConditionOp
    {
        // Short-circuits, so the right-hand condition is not evaluated when the left one is already false.
        public bool Evaluate(ref SlotContext ctx) => ctx.GetBool(0) && ctx.GetBool(1);
    }

    /// <summary><c>operator.or</c> — true when either slot is true.</summary>
    [BlockExecutor("operator.or")]
    public sealed class OrCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) => ctx.GetBool(0) || ctx.GetBool(1);
    }

    /// <summary><c>operator.not</c> — true when the slot is false, so <c>not (touching?)</c> reads as "while it is clear".</summary>
    [BlockExecutor("operator.not")]
    public sealed class NotCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) => !ctx.GetBool(0);
    }
}
