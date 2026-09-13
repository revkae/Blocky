using UnityEngine;

namespace Blocky.Data
{
    public enum ChainTargetKind
    {
        Free,
        Insert,
        AttachAbove,
        Discard,
        ConditionSlot
    }

    /// <summary>
    /// Where a dragged chain of blocks lands (<see cref="DropChain"/>): loose on the table at a canvas position,
    /// inserted into a sequence, attached on top of a loose stack (the chain's bottom connects to that stack's
    /// top block), dropped into a block's condition slot (a single condition block only), or thrown away
    /// (dropped back on the palette).
    /// </summary>
    public readonly struct ChainTarget
    {
        public readonly ChainTargetKind Kind;
        public readonly Vector2 Position;      // Free: the new stack's position. AttachAbove: the merged stack's new top-left. ConditionSlot: where a displaced condition is put.
        public readonly NodeLocation InsertAt; // Insert only
        public readonly string StackId;        // AttachAbove, ConditionSlot
        public readonly string NodeId;         // ConditionSlot: the block that owns the slot
        public readonly string ParamKey;       // ConditionSlot: which of its params the slot is

        private ChainTarget(ChainTargetKind kind, Vector2 position, NodeLocation insertAt, string stackId, string nodeId = null, string paramKey = null)
        {
            Kind = kind;
            Position = position;
            InsertAt = insertAt;
            StackId = stackId;
            NodeId = nodeId;
            ParamKey = paramKey;
        }

        public static ChainTarget Free(Vector2 position) => new(ChainTargetKind.Free, position, default, null);

        public static ChainTarget Insert(NodeLocation at) => new(ChainTargetKind.Insert, default, at, null);

        public static ChainTarget AttachAbove(string stackId, Vector2 newTopPosition) =>
            new(ChainTargetKind.AttachAbove, newTopPosition, default, stackId);

        public static ChainTarget Discard() => new(ChainTargetKind.Discard, default, default, null);

        /// <param name="ejectPosition">If the slot is already filled, the condition there is popped out onto the table here.</param>
        public static ChainTarget IntoConditionSlot(string stackId, string ownerNodeId, string paramKey, Vector2 ejectPosition) =>
            new(ChainTargetKind.ConditionSlot, ejectPosition, default, stackId, ownerNodeId, paramKey);
    }
}
