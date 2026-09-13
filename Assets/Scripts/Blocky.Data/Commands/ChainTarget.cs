using UnityEngine;

namespace Blocky.Data
{
    public enum ChainTargetKind
    {
        Free,
        Insert,
        AttachAbove,
        Discard
    }

    /// <summary>
    /// Where a dragged chain of blocks lands (<see cref="DropChain"/>): loose on the table at a canvas position,
    /// inserted into a sequence, attached on top of a loose stack (the chain's bottom connects to that stack's
    /// top block), or thrown away (dropped back on the palette).
    /// </summary>
    public readonly struct ChainTarget
    {
        public readonly ChainTargetKind Kind;
        public readonly Vector2 Position;      // Free: the new stack's position. AttachAbove: the merged stack's new top-left.
        public readonly NodeLocation InsertAt; // Insert only
        public readonly string StackId;        // AttachAbove only

        private ChainTarget(ChainTargetKind kind, Vector2 position, NodeLocation insertAt, string stackId)
        {
            Kind = kind;
            Position = position;
            InsertAt = insertAt;
            StackId = stackId;
        }

        public static ChainTarget Free(Vector2 position) => new(ChainTargetKind.Free, position, default, null);

        public static ChainTarget Insert(NodeLocation at) => new(ChainTargetKind.Insert, default, at, null);

        public static ChainTarget AttachAbove(string stackId, Vector2 newTopPosition) =>
            new(ChainTargetKind.AttachAbove, newTopPosition, default, stackId);

        public static ChainTarget Discard() => new(ChainTargetKind.Discard, default, default, null);
    }
}
