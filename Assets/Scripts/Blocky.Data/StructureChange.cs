namespace Blocky.Data
{
    public enum StructureChangeKind
    {
        NodeInserted,
        NodeRemoved,
        NodeMoved,
        ParamChanged,
        StackCreated,
        StackDeleted,
        StackMoved
    }

    /// <summary>Diff raised by <see cref="ProgramStore.OnChanged"/> so a view can rebuild one subtree, not the whole canvas.</summary>
    public readonly struct StructureChange
    {
        public readonly string StackId;
        public readonly string NodeId; // null when the change is stack-level
        public readonly StructureChangeKind Kind;

        public StructureChange(string stackId, string nodeId, StructureChangeKind kind)
        {
            StackId = stackId;
            NodeId = nodeId;
            Kind = kind;
        }
    }
}
