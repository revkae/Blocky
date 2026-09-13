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
        Cap
    }
}
