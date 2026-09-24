namespace Blocky.Compiler
{
    /// <summary>Visual/structural shape of a block. TDD §7.</summary>
    public enum BlockShape
    {
        // Statement is 0 (the default) deliberately: a BlockDefinition where an author forgot to set `shape`
        // should fail loudly at OpTableBuilder.Build time ("no IBlockOp bound"), not silently be skipped as
        // if it were a Trigger — see Decisions/ADR-004.
        Statement,
        Trigger,
        CBlock,
        Cap,

        /// <summary>
        /// A condition (Scratch's hexagonal boolean reporter): answers true/false and never runs as a step. It has
        /// no notch or tab, so it can't join a sequence — it only fits a condition slot (a <c>Reporter</c> param)
        /// of blocks like <c>if</c> and <c>repeat until</c>. Bound to an <c>IConditionOp</c>, not an <c>IBlockOp</c>.
        /// Appended last so existing assets' serialized shape values keep their meaning.
        /// </summary>
        Boolean,

        /// <summary>
        /// A reporter (Scratch's round block): answers a value — a number or a string — and never runs as a step.
        /// It fits any white input oval, and a <see cref="Boolean"/> fits there too (it reads as true/false).
        /// Bound to an <c>IValueOp</c>. Appended after <see cref="Boolean"/>, again so no stored value shifts.
        /// </summary>
        Reporter
    }
}
