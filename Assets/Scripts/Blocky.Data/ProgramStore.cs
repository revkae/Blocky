using System;
using System.Collections.Generic;

namespace Blocky.Data
{
    /// <summary>
    /// Owns one <see cref="ObjectProgram"/> and its command history. The undo stack is implemented but capped
    /// at 0 entries by default in v1 (TDD §4.4, §1.2) — set <see cref="UndoCapacity"/> to enable it later.
    /// Undone commands move to a redo stack and can be applied again; any fresh edit clears it, the standard
    /// rule — once history has branched, the old future is no longer reachable from what's on the table.
    /// </summary>
    public sealed class ProgramStore
    {
        private readonly List<IProgramCommand> _undoStack = new();
        private readonly List<IProgramCommand> _redoStack = new();

        public ObjectProgram Program { get; }
        public event Action<StructureChange> OnChanged;
        public int UndoCapacity { get; set; } = 0;

        public ProgramStore(ObjectProgram program)
        {
            Program = program ?? throw new ArgumentNullException(nameof(program));
        }

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        public void Apply(IProgramCommand cmd)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            cmd.Do(this);

            _redoStack.Clear(); // a new edit branches the history: what was undone can't be reached from here any more
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
            _redoStack.Add(cmd);
        }

        /// <summary>
        /// Puts back the last undone edit. Commands re-run through <see cref="IProgramCommand.Do"/> rather than a
        /// stored "after" snapshot: every one of them resolves what it touches from the program as it is now, and
        /// undo has put that back exactly as it was before the command ran.
        /// </summary>
        public void Redo()
        {
            if (!CanRedo) return;
            var cmd = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            cmd.Do(this);

            if (UndoCapacity <= 0) return;
            _undoStack.Add(cmd);
            if (_undoStack.Count > UndoCapacity)
                _undoStack.RemoveAt(0);
        }

        internal void RaiseChanged(StructureChange change) => OnChanged?.Invoke(change);
    }
}
