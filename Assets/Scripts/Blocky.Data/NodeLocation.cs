namespace Blocky.Data
{
    /// <summary>
    /// Addresses one slot in a program: either index <see cref="Index"/> of a stack's top-level sequence
    /// (<see cref="ParentNodeId"/> null), or index <see cref="Index"/> of branch <see cref="BranchIndex"/>
    /// of node <see cref="ParentNodeId"/>.
    /// </summary>
    public readonly struct NodeLocation
    {
        public readonly string StackId;
        public readonly string ParentNodeId;
        public readonly int BranchIndex;
        public readonly int Index;

        public NodeLocation(string stackId, string parentNodeId, int branchIndex, int index)
        {
            StackId = stackId;
            ParentNodeId = parentNodeId;
            BranchIndex = branchIndex;
            Index = index;
        }

        public static NodeLocation InStack(string stackId, int index) => new(stackId, null, -1, index);

        public static NodeLocation InBranch(string stackId, string parentNodeId, int branchIndex, int index) =>
            new(stackId, parentNodeId, branchIndex, index);
    }

    /// <summary>Addresses a param array: a node's <c>parameters</c>, or (NodeId null) a stack's <c>triggerParameters</c>.</summary>
    public readonly struct ParamTarget
    {
        public readonly string StackId;
        public readonly string NodeId;

        public ParamTarget(string stackId, string nodeId)
        {
            StackId = stackId;
            NodeId = nodeId;
        }
    }
}
