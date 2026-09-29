using System;
using UnityEngine;

namespace Blocky.Data
{
    public sealed class MoveStack : IProgramCommand
    {
        private readonly string _stackId;
        private readonly Vector2 _newPosition;
        private Vector2 _oldPosition;

        public MoveStack(string stackId, Vector2 newPosition)
        {
            _stackId = stackId;
            _newPosition = newPosition;
        }

        public void Do(ProgramStore store)
        {
            var stack = Find(store.Program);
            _oldPosition = stack.canvasPosition;
            stack.canvasPosition = _newPosition;
            store.RaiseChanged(new StructureChange(_stackId, null, StructureChangeKind.StackMoved));
        }

        public void Undo(ProgramStore store)
        {
            Find(store.Program).canvasPosition = _oldPosition;
            store.RaiseChanged(new StructureChange(_stackId, null, StructureChangeKind.StackMoved));
        }

        private BlockStack Find(ObjectProgram program) =>
            ProgramQuery.FindStack(program, _stackId)
                ?? throw new InvalidOperationException($"Stack '{_stackId}' not found.");

        public string Describe() => "Move stack";
    }
}
