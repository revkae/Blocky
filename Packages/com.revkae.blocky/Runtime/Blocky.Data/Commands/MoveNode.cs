namespace Blocky.Data
{
    /// <summary>
    /// Removes from <c>from</c> then inserts at <c>to</c>. When both locations share a container, <c>to.Index</c>
    /// must already account for the shift caused by the removal — callers compute this, the command does not guess.
    /// </summary>
    public sealed class MoveNode : IProgramCommand
    {
        private readonly NodeLocation _from;
        private readonly NodeLocation _to;
        private BlockNode _node;

        public MoveNode(NodeLocation from, NodeLocation to)
        {
            _from = from;
            _to = to;
        }

        public void Do(ProgramStore store)
        {
            var fromContainer = ProgramQuery.Resolve(store.Program, _from);
            fromContainer.Set(ArrayUtil.RemoveAt(fromContainer.Get(), _from.Index, out _node));

            var toContainer = ProgramQuery.Resolve(store.Program, _to);
            toContainer.Set(ArrayUtil.Insert(toContainer.Get(), _to.Index, _node));

            store.RaiseChanged(new StructureChange(_to.StackId, _node.id, StructureChangeKind.NodeMoved));
        }

        public void Undo(ProgramStore store)
        {
            var toContainer = ProgramQuery.Resolve(store.Program, _to);
            toContainer.Set(ArrayUtil.RemoveAt(toContainer.Get(), _to.Index, out _));

            var fromContainer = ProgramQuery.Resolve(store.Program, _from);
            fromContainer.Set(ArrayUtil.Insert(fromContainer.Get(), _from.Index, _node));

            store.RaiseChanged(new StructureChange(_from.StackId, _node.id, StructureChangeKind.NodeMoved));
        }

        public string Describe() => "Move node";
    }
}
