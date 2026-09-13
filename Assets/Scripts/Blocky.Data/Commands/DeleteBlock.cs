using System;

namespace Blocky.Data
{
    /// <summary>
    /// Deletes the one block the user selected on the table. A node goes alone (a C-block takes its mouth
    /// contents with it) and the blocks below it close up; a loose stack left empty disappears. Deleting a hat
    /// keeps the blocks that were under it as a loose stack — the user deleted the event, not the script body.
    /// </summary>
    public sealed class DeleteBlock : IProgramCommand
    {
        private readonly string _stackId;
        private readonly string _nodeId;
        private BlockStack[] _before;

        /// <param name="nodeId">The node to delete, or null to delete the stack's hat.</param>
        public DeleteBlock(string stackId, string nodeId)
        {
            _stackId = stackId;
            _nodeId = nodeId;
        }

        public void Do(ProgramStore store)
        {
            var program = store.Program;
            var stack = ProgramQuery.FindStack(program, _stackId)
                ?? throw new InvalidOperationException($"Stack '{_stackId}' not found.");
            _before = ProgramEdits.Snapshot(program);

            if (_nodeId == null)
            {
                if (stack.sequence.Length == 0)
                {
                    ProgramEdits.RemoveStack(program, _stackId);
                }
                else
                {
                    stack.triggerBlockType = string.Empty;
                    stack.triggerParameters = Array.Empty<BlockParam>();
                }
            }
            else
            {
                var location = ProgramQuery.FindLocation(program, _stackId, _nodeId)
                    ?? throw new InvalidOperationException($"Node '{_nodeId}' not found in stack '{_stackId}'.");
                var container = ProgramQuery.Resolve(program, location);
                container.Set(ArrayUtil.RemoveAt(container.Get(), location.Index, out _));

                if (ProgramQuery.IsLoose(stack) && stack.sequence.Length == 0) ProgramEdits.RemoveStack(program, _stackId);
            }

            store.RaiseChanged(new StructureChange(_stackId, _nodeId, StructureChangeKind.NodeRemoved));
        }

        public void Undo(ProgramStore store)
        {
            store.Program.stacks = _before;
            store.RaiseChanged(new StructureChange(_stackId, _nodeId, StructureChangeKind.NodeInserted));
        }

        public string Describe() => _nodeId == null ? "Delete hat block" : "Delete block";
    }
}
