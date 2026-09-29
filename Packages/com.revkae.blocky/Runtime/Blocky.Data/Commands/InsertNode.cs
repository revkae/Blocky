namespace Blocky.Data
{
    public sealed class InsertNode : IProgramCommand
    {
        private readonly NodeLocation _location;
        private readonly BlockNode _node;

        public InsertNode(NodeLocation location, BlockNode node)
        {
            _location = location;
            _node = node;
        }

        public void Do(ProgramStore store)
        {
            var container = ProgramQuery.Resolve(store.Program, _location);
            container.Set(ArrayUtil.Insert(container.Get(), _location.Index, _node));
            store.RaiseChanged(new StructureChange(_location.StackId, _node.id, StructureChangeKind.NodeInserted));
        }

        public void Undo(ProgramStore store)
        {
            var container = ProgramQuery.Resolve(store.Program, _location);
            container.Set(ArrayUtil.RemoveAt(container.Get(), _location.Index, out _));
            store.RaiseChanged(new StructureChange(_location.StackId, _node.id, StructureChangeKind.NodeRemoved));
        }

        public string Describe() => $"Insert {_node.blockType}";
    }
}
