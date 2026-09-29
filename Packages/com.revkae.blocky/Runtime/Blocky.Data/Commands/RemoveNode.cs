namespace Blocky.Data
{
    public sealed class RemoveNode : IProgramCommand
    {
        private readonly NodeLocation _location;
        private BlockNode _removed;

        public RemoveNode(NodeLocation location)
        {
            _location = location;
        }

        public void Do(ProgramStore store)
        {
            var container = ProgramQuery.Resolve(store.Program, _location);
            container.Set(ArrayUtil.RemoveAt(container.Get(), _location.Index, out _removed));
            store.RaiseChanged(new StructureChange(_location.StackId, _removed.id, StructureChangeKind.NodeRemoved));
        }

        public void Undo(ProgramStore store)
        {
            var container = ProgramQuery.Resolve(store.Program, _location);
            container.Set(ArrayUtil.Insert(container.Get(), _location.Index, _removed));
            store.RaiseChanged(new StructureChange(_location.StackId, _removed.id, StructureChangeKind.NodeInserted));
        }

        public string Describe() => "Remove node";
    }
}
