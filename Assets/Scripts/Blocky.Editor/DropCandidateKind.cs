namespace Blocky.Editor
{
    /// <summary>TDD §8.3. Ties resolve innermost-first, so dropping onto a cavity inside a stack nests rather than splices.</summary>
    public enum DropCandidateKind
    {
        BodyCavity,
        SequenceGap,
        SequenceEnd,
        Canvas
    }
}
