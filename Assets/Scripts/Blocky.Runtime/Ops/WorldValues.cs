namespace Blocky.Runtime.Ops
{
    /// <summary><c>sensing.timer</c> — the timer as a number, for arithmetic and comparisons rather than the fixed <c>timer &gt; n?</c> question.</summary>
    [BlockExecutor("sensing.timer")]
    public sealed class TimerValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) => BlockValue.Number(ctx.Timer);
    }

    /// <summary>
    /// <c>motion.position</c> — this object's x, y or z in world space. The choice is read by index, so the order
    /// in the asset (x, y, z) is part of the contract. With this, "move a little further each time" is just
    /// <c>set position ((position x) + (1)) …</c>.
    /// </summary>
    [BlockExecutor("motion.position")]
    public sealed class PositionValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            if (ctx.Target == null) return BlockValue.Number(0f);
            var position = ctx.Target.transform.position;
            return BlockValue.Number(ctx.Params[0].ChoiceIndex switch { 1 => position.y, 2 => position.z, _ => position.x });
        }
    }

    /// <summary><c>motion.rotation</c> — this object's rotation around x, y or z, in degrees (0–360, as the inspector shows it).</summary>
    [BlockExecutor("motion.rotation")]
    public sealed class RotationValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            if (ctx.Target == null) return BlockValue.Number(0f);
            var euler = ctx.Target.transform.eulerAngles;
            return BlockValue.Number(ctx.Params[0].ChoiceIndex switch { 1 => euler.y, 2 => euler.z, _ => euler.x });
        }
    }
}
