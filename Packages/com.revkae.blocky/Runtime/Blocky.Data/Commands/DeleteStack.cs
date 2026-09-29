using System;

namespace Blocky.Data
{
    public sealed class DeleteStack : IProgramCommand
    {
        private readonly string _stackId;
        private BlockStack _removed;
        private int _index;

        public DeleteStack(string stackId)
        {
            _stackId = stackId;
        }

        public void Do(ProgramStore store)
        {
            _index = Array.FindIndex(store.Program.stacks, s => s.id == _stackId);
            if (_index < 0) throw new InvalidOperationException($"Stack '{_stackId}' not found.");
            store.Program.stacks = ArrayUtil.RemoveAt(store.Program.stacks, _index, out _removed);
            store.RaiseChanged(new StructureChange(_stackId, null, StructureChangeKind.StackDeleted));
        }

        public void Undo(ProgramStore store)
        {
            store.Program.stacks = ArrayUtil.Insert(store.Program.stacks, _index, _removed);
            store.RaiseChanged(new StructureChange(_stackId, null, StructureChangeKind.StackCreated));
        }

        public string Describe() => "Delete stack";
    }
}
