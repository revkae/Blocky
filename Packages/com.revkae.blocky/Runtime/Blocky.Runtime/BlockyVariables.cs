using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Named values a program can set and read back — Scratch's variables and lists. Two scopes, as in Scratch: one
    /// shared by everything in the scene ("for all sprites"), and one private to each object ("for this sprite
    /// only"), which is what lets ten copies of the same program each count their own score.
    /// Names are matched case-insensitively and trimmed: a learner typing "Score" and "score " means one variable.
    /// A variable that was never set reads as empty — 0 as a number, "" as text — and a list that was never used
    /// reads as a list with nothing in it, rather than either being an error.
    /// </summary>
    public sealed class BlockyVariables
    {
        /// <summary>
        /// The most items one list holds. <c>repeat forever { add … }</c> is a program a learner will write, and it
        /// should stop growing, not take the machine with it — the same reasoning as the clone cap.
        /// </summary>
        public const int MaxListLength = 10000;

        /// <summary>One scope's worth of data: the shared one, or one object's own.</summary>
        private sealed class Store
        {
            public readonly Dictionary<string, BlockValue> Values = new(System.StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, List<BlockValue>> Lists = new(System.StringComparer.OrdinalIgnoreCase);
        }

        private readonly Store _shared = new();
        private readonly Dictionary<GameObject, Store> _perObject = new();
        private readonly List<GameObject> _destroyed = new(); // reused, so pruning allocates nothing

        /// <summary>Every shared variable, in the order it was first set — what an on-screen watcher lists.</summary>
        public IReadOnlyDictionary<string, BlockValue> Shared => _shared.Values;

        /// <summary>
        /// Every shared list, in the order it was first changed — what the watcher card lists. For reading only:
        /// the blocks change lists through the methods below, which keep the cap and the index rules.
        /// </summary>
        public IReadOnlyDictionary<string, List<BlockValue>> SharedLists => _shared.Lists;

        // ---- variables ---------------------------------------------------------------------------------

        public BlockValue Get(string name, GameObject owner = null)
        {
            var key = Key(name);
            if (key.Length == 0) return BlockValue.Empty;

            var store = Find(owner);
            return store != null && store.Values.TryGetValue(key, out var value) ? value : BlockValue.Empty;
        }

        public void Set(string name, BlockValue value, GameObject owner = null)
        {
            var key = Key(name);
            if (key.Length == 0) return;

            FindOrCreate(owner).Values[key] = value;
        }

        /// <summary>Adds to a variable, reading whatever is there as a number — so "change score by 1" works before it has ever been set.</summary>
        public void Change(string name, float amount, GameObject owner = null) =>
            Set(name, BlockValue.Number(Get(name, owner).AsNumber() + amount), owner);

        // ---- lists -------------------------------------------------------------------------------------
        // Positions count from 1, as in Scratch, and are rounded down, so "item 2.7" is item 2. A position that
        // isn't in the list does nothing when changing it and reads as empty — a typo does nothing, it never
        // throws. Every change makes the list if it doesn't exist yet (so "delete all of [items]" at the start of a
        // program shows an empty list on the watcher); reading never makes one.

        /// <summary>How many items the list has; 0 for a list that was never used.</summary>
        public int Length(string name, GameObject owner = null) => FindList(name, owner)?.Count ?? 0;

        /// <summary>Item <paramref name="position"/> (from 1), or empty when the list has no such item.</summary>
        public BlockValue Item(string name, float position, GameObject owner = null)
        {
            var list = FindList(name, owner);
            if (list == null) return BlockValue.Empty;

            var index = IndexOf(position, list.Count);
            return index < 0 ? BlockValue.Empty : list[index];
        }

        /// <summary>Adds <paramref name="item"/> to the end. Does nothing once the list is <see cref="MaxListLength"/> long.</summary>
        public void Add(string name, BlockValue item, GameObject owner = null)
        {
            var list = FindOrCreateList(name, owner);
            if (list != null && list.Count < MaxListLength) list.Add(item);
        }

        /// <summary>
        /// Puts <paramref name="item"/> at <paramref name="position"/> and moves the rest along. One past the last
        /// item is allowed, and adds to the end — so inserting at 1 into an empty list works.
        /// </summary>
        public void Insert(string name, float position, BlockValue item, GameObject owner = null)
        {
            var list = FindOrCreateList(name, owner);
            if (list == null || list.Count >= MaxListLength) return;

            var index = IndexOf(position, list.Count + 1);
            if (index >= 0) list.Insert(index, item);
        }

        /// <summary>Removes item <paramref name="position"/>; the ones after it move up one.</summary>
        public void DeleteAt(string name, float position, GameObject owner = null)
        {
            var list = FindOrCreateList(name, owner);
            if (list == null) return;

            var index = IndexOf(position, list.Count);
            if (index >= 0) list.RemoveAt(index);
        }

        /// <summary>Empties the list, which still exists afterwards — the usual first block of a program that fills one.</summary>
        public void DeleteAll(string name, GameObject owner = null) => FindOrCreateList(name, owner)?.Clear();

        /// <summary>Changes item <paramref name="position"/> to <paramref name="item"/>. The list keeps its length.</summary>
        public void Replace(string name, float position, BlockValue item, GameObject owner = null)
        {
            var list = FindOrCreateList(name, owner);
            if (list == null) return;

            var index = IndexOf(position, list.Count);
            if (index >= 0) list[index] = item;
        }

        /// <summary>
        /// Where <paramref name="item"/> first appears (from 1), or 0 when it isn't there. Items match the way
        /// <c>=</c> matches: as numbers when both look like numbers, otherwise as words ignoring case — so 10
        /// finds "10.0", and "apple" finds "Apple".
        /// </summary>
        public int PositionOf(string name, BlockValue item, GameObject owner = null)
        {
            var list = FindList(name, owner);
            if (list == null) return 0;

            for (var i = 0; i < list.Count; i++)
                if (Ops.ValueComparison.Compare(list[i], item) == 0)
                    return i + 1;
            return 0;
        }

        /// <summary>True when <see cref="PositionOf"/> would find the item.</summary>
        public bool Contains(string name, BlockValue item, GameObject owner = null) => PositionOf(name, item, owner) > 0;

        // ---- both --------------------------------------------------------------------------------------

        /// <summary>Forgets everything. A new play session starts from nothing; <see cref="BlockyRuntime"/> rebuilds this.</summary>
        public void Clear()
        {
            _shared.Values.Clear();
            _shared.Lists.Clear();
            _perObject.Clear();
        }

        private static string Key(string name) => string.IsNullOrEmpty(name) ? string.Empty : name.Trim();

        /// <summary>A 1-based, possibly fractional position as a 0-based index, or -1 when it isn't one of <paramref name="count"/>.</summary>
        private static int IndexOf(float position, int count)
        {
            if (float.IsNaN(position) || position < 1f || position >= count + 1f) return -1;
            return (int)position - 1;
        }

        private Store Find(GameObject owner)
        {
            if (owner == null) return _shared;
            return _perObject.TryGetValue(owner, out var mine) ? mine : null;
        }

        private Store FindOrCreate(GameObject owner)
        {
            if (owner == null) return _shared;
            if (_perObject.TryGetValue(owner, out var mine)) return mine;

            Prune();
            return _perObject[owner] = new Store();
        }

        private List<BlockValue> FindList(string name, GameObject owner)
        {
            var key = Key(name);
            if (key.Length == 0) return null;

            var store = Find(owner);
            return store != null && store.Lists.TryGetValue(key, out var list) ? list : null;
        }

        private List<BlockValue> FindOrCreateList(string name, GameObject owner)
        {
            var key = Key(name);
            if (key.Length == 0) return null;

            var lists = FindOrCreate(owner).Lists;
            if (!lists.TryGetValue(key, out var list)) lists[key] = list = new List<BlockValue>();
            return list;
        }

        /// <summary>Drops the variables and lists of objects that no longer exist. Only runs when a new object first stores one.</summary>
        private void Prune()
        {
            foreach (var pair in _perObject)
                if (pair.Key == null) _destroyed.Add(pair.Key);

            if (_destroyed.Count == 0) return;
            foreach (var dead in _destroyed) _perObject.Remove(dead);
            _destroyed.Clear();
        }
    }
}
