namespace Blocky.Compiler
{
    /// <summary>Drives block color via a USS class in the editor (TDD §7, §9). Extend as the catalog grows — append only, assets store the number.</summary>
    public enum BlockCategory
    {
        Event,
        Motion,
        Looks,
        Control,
        Conditions
    }
}
