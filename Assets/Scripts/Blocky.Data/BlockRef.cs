using System;

namespace Blocky.Data
{
    /// <summary>
    /// Names one block on the table: a node (<see cref="NodeId"/>), or with a null node id the hat of stack
    /// <see cref="StackId"/>. Node ids are unique across the program, so two refs to the same node are equal even
    /// if one still carries the stack the node sat in before a move.
    /// </summary>
    public readonly struct BlockRef : IEquatable<BlockRef>
    {
        public readonly string StackId;

        /// <summary>The node, or null for the stack's hat.</summary>
        public readonly string NodeId;

        public BlockRef(string stackId, string nodeId)
        {
            StackId = stackId;
            NodeId = nodeId;
        }

        public bool IsHat => NodeId == null;

        public bool Equals(BlockRef other) => IsHat ? other.IsHat && StackId == other.StackId : NodeId == other.NodeId;

        public override bool Equals(object obj) => obj is BlockRef other && Equals(other);

        public override int GetHashCode() => IsHat ? HashCode.Combine(1, StackId) : HashCode.Combine(2, NodeId);

        public override string ToString() => IsHat ? $"hat of {StackId}" : $"{NodeId} in {StackId}";
    }
}
