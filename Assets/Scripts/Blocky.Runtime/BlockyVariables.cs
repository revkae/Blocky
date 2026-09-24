using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Named values a program can set and read back — Scratch's variables. Two scopes, as in Scratch: one shared
    /// by everything in the scene ("for all sprites"), and one private to each object ("for this sprite only"),
    /// which is what lets ten copies of the same program each count their own score.
    /// Names are matched case-insensitively and trimmed: a learner typing "Score" and "score " means one variable.
    /// A variable that was never set reads as empty — 0 as a number, "" as text — rather than being an error.
    /// </summary>
    public sealed class BlockyVariables
    {
        private readonly Dictionary<string, BlockValue> _shared = new(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<GameObject, Dictionary<string, BlockValue>> _perObject = new();
        private readonly List<GameObject> _destroyed = new(); // reused, so pruning allocates nothing

        /// <summary>Every shared variable, in the order it was first set — what an on-screen watcher lists.</summary>
        public IReadOnlyDictionary<string, BlockValue> Shared => _shared;

        public BlockValue Get(string name, GameObject owner = null)
        {
            var key = Key(name);
            if (key.Length == 0) return BlockValue.Empty;

            if (owner == null) return _shared.TryGetValue(key, out var shared) ? shared : BlockValue.Empty;

            return _perObject.TryGetValue(owner, out var mine) && mine.TryGetValue(key, out var value) ? value : BlockValue.Empty;
        }

        public void Set(string name, BlockValue value, GameObject owner = null)
        {
            var key = Key(name);
            if (key.Length == 0) return;

            if (owner == null)
            {
                _shared[key] = value;
                return;
            }

            if (!_perObject.TryGetValue(owner, out var mine))
            {
                Prune();
                _perObject[owner] = mine = new Dictionary<string, BlockValue>(System.StringComparer.OrdinalIgnoreCase);
            }
            mine[key] = value;
        }

        /// <summary>Adds to a variable, reading whatever is there as a number — so "change score by 1" works before it has ever been set.</summary>
        public void Change(string name, float amount, GameObject owner = null) =>
            Set(name, BlockValue.Number(Get(name, owner).AsNumber() + amount), owner);

        /// <summary>Forgets everything. A new play session starts from nothing; <see cref="BlockyRuntime"/> rebuilds this.</summary>
        public void Clear()
        {
            _shared.Clear();
            _perObject.Clear();
        }

        private static string Key(string name) => string.IsNullOrEmpty(name) ? string.Empty : name.Trim();

        /// <summary>Drops the variables of objects that no longer exist. Only runs when a new object first sets one.</summary>
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
