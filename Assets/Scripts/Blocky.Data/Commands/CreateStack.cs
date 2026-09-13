using System;

namespace Blocky.Data
{
    public sealed class CreateStack : IProgramCommand
    {
        private readonly BlockStack _stack;

        public CreateStack(BlockStack stack)
        {
            _stack = stack;
        }

        public void Do(ProgramStore store)
        {
            store.Program.stacks = ArrayUtil.Insert(store.Program.stacks, store.Program.stacks.Length, _stack);
            store.RaiseChanged(new StructureChange(_stack.id, null, StructureChangeKind.StackCreated));
        }

        public void Undo(ProgramStore store)
        {
            var index = Array.FindIndex(store.Program.stacks, s => s.id == _stack.id);
            store.Program.stacks = ArrayUtil.RemoveAt(store.Program.stacks, index, out _);
            store.RaiseChanged(new StructureChange(_stack.id, null, StructureChangeKind.StackDeleted));
        }

        public string Describe() => $"Create stack {_stack.triggerBlockType}";
    }
}
