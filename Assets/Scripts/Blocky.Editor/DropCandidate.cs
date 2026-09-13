using UnityEngine;

namespace Blocky.Editor
{
    /// <summary>
    /// One drop target, collected once at drag start and rebuilt only on structural change (TDD §8.3).
    /// <see cref="Depth"/> is 0 for the top-level stack sequence and +1 per nested body-slot — used to enforce
    /// the nesting-depth cap (TDD §8.4).
    /// </summary>
    public readonly struct DropCandidate
    {
        public readonly Rect Rect;
        public readonly DropCandidateKind Kind;
        public readonly string StackId;
        public readonly string ParentNodeId; // null for a top-level sequence candidate or a Canvas candidate
        public readonly int BranchIndex;     // -1 when not inside a body-slot
        public readonly int Index;           // insertion index within the target sequence; unused for Canvas
        public readonly int Depth;

        public DropCandidate(Rect rect, DropCandidateKind kind, string stackId, string parentNodeId, int branchIndex, int index, int depth)
        {
            Rect = rect;
            Kind = kind;
            StackId = stackId;
            ParentNodeId = parentNodeId;
            BranchIndex = branchIndex;
            Index = index;
            Depth = depth;
        }
    }
}
