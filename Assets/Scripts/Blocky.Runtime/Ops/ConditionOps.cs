namespace Blocky.Runtime.Ops
{
    /// <summary><c>condition.mouse_down</c> — true while the left mouse button is held over the game (not over the editor).</summary>
    [BlockExecutor("condition.mouse_down")]
    public sealed class MouseDownCondition : IConditionOp
    {
        public bool Evaluate(ref ConditionContext ctx) => BlockyInput.IsMouseDown;
    }

    /// <summary><c>condition.mouse_up</c> — the opposite of <c>mouse down?</c>: true while the button is not held over the game.</summary>
    [BlockExecutor("condition.mouse_up")]
    public sealed class MouseUpCondition : IConditionOp
    {
        public bool Evaluate(ref ConditionContext ctx) => !BlockyInput.IsMouseDown;
    }

    /// <summary><c>condition.true</c> — always true (e.g. an <c>if</c> you want to switch on while testing).</summary>
    [BlockExecutor("condition.true")]
    public sealed class TrueCondition : IConditionOp
    {
        public bool Evaluate(ref ConditionContext ctx) => true;
    }

    /// <summary><c>condition.false</c> — always false.</summary>
    [BlockExecutor("condition.false")]
    public sealed class FalseCondition : IConditionOp
    {
        public bool Evaluate(ref ConditionContext ctx) => false;
    }
}
