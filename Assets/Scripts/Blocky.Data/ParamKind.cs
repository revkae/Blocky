namespace Blocky.Data
{
    /// <summary>Tagged union discriminator for <see cref="BlockParam"/>. See TDD §4.1.</summary>
    public enum ParamKind : byte
    {
        Number = 0,
        Text = 1,
        Bool = 2,
        Choice = 3,
        ObjectRef = 4,
        Reporter = 5
    }
}
