using System;
using System.Collections.Generic;

namespace Blocky.Data
{
    /// <summary>
    /// Owns one <see cref="ObjectProgram"/> and its command history. The undo stack is implemented but capped
    /// at 0 entries by default in v1 (TDD §4.4, §1.2) — set <see cref="UndoCapacity"/> to enable it later.
    /// </summary>
    public sealed class ProgramStore
    {
        private readonly List<IProgramCommand> _undoStack = new();

        public ObjectProgram Program { get; }
        public event Action<StructureChange> OnChanged;
        public int UndoCapacity { get; set; } = 0;

        public ProgramStore(ObjectProgram program)
        {
            Program = program ?? throw new ArgumentNullException(nameof(program));
        }

        public bool CanUndo => _undoStack.Count > 0;

        public void Apply(IProgramCommand cmd)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            cmd.Do(this);

            if (UndoCapacity <= 0) return;
            _undoStack.Add(cmd);
            if (_undoStack.Count > UndoCapacity)
                _undoStack.RemoveAt(0);
        }

        public void Undo()
        {
            if (!CanUndo) return;
            var cmd = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            cmd.Undo(this);
        }

        internal void RaiseChanged(StructureChange change) => OnChanged?.Invoke(change);
    }
}
