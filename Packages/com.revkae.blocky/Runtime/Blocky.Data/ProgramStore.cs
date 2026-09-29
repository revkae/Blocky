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
        private readonly List<StructureChange> _heldChanges = new();
        private bool _holdingChanges;

        public ObjectProgram Program { get; }
        public event Action<StructureChange> OnChanged;
        public int UndoCapacity { get; set; } = 0;

        /// <summary>
        /// The most blocks the program may have, counted as <see cref="ProgramQuery.CountBlocks"/> does (hats don't
        /// count) — a level's "solve it in 5 blocks". 0: no limit. An edit that would take the program over it, or
        /// further over, is refused: <see cref="Apply"/> returns false, the program is left exactly as it was and
        /// <see cref="OnChanged"/> never hears of it. Edits that don't add blocks are always allowed, so a program
        /// already over the limit can still be tidied and trimmed.
        /// </summary>
        public int BlockLimit { get; set; }

        /// <summary>An edit was refused because of <see cref="BlockLimit"/> — so the editor can say why nothing happened.</summary>
        public event Action LimitRefused;

        /// <summary>
        /// Which blocks may be added, by block type — a level's toolbox. Null: any. Views that make a block on their
        /// own (the ⬡ hole's menu) offer only these; the palette is filtered by its host.
        /// </summary>
        public Func<string, bool> Offers { get; set; }

        public ProgramStore(ObjectProgram program)
        {
            Program = program ?? throw new ArgumentNullException(nameof(program));
        }

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>Runs <paramref name="cmd"/> and records it for Undo. False when <see cref="BlockLimit"/> refused it.</summary>
        public bool Apply(IProgramCommand cmd)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));

            if (BlockLimit <= 0) cmd.Do(this);
            else if (!DoWithinLimit(cmd)) return false;

            _redoStack.Clear(); // a new edit branches the history: what was undone can't be reached from here any more
            if (UndoCapacity <= 0) return true;
            _undoStack.Add(cmd);
            if (_undoStack.Count > UndoCapacity)
                _undoStack.RemoveAt(0);
            return true;
        }

        /// <summary>
        /// Runs the command with its change notices held back, and keeps it only if it doesn't add blocks past the
        /// limit; otherwise undoes it — still held back — so listeners never see the program change at all.
        /// </summary>
        private bool DoWithinLimit(IProgramCommand cmd)
        {
            var before = ProgramQuery.CountBlocks(Program);
            _holdingChanges = true;
            try { cmd.Do(this); }
            finally { _holdingChanges = false; }

            var after = ProgramQuery.CountBlocks(Program);
            if (after > BlockLimit && after > before)
            {
                _holdingChanges = true;
                try { cmd.Undo(this); }
                finally { _holdingChanges = false; }
                _heldChanges.Clear();
                LimitRefused?.Invoke();
                return false;
            }

            var changes = _heldChanges.ToArray(); // a listener may edit again, which would add to the list mid-loop
            _heldChanges.Clear();
            foreach (var change in changes) OnChanged?.Invoke(change);
            return true;
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

        internal void RaiseChanged(StructureChange change)
        {
            if (_holdingChanges) _heldChanges.Add(change);
            else OnChanged?.Invoke(change);
        }
    }
}
