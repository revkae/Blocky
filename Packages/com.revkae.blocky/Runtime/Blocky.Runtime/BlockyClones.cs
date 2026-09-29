using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Marks an object that was made by <c>create clone of</c>. Its only job is to answer "is this a clone?", which
    /// is what lets <c>delete this clone</c> refuse to delete the original — the same rule Scratch has.
    /// </summary>
    public sealed class BlockyClone : MonoBehaviour
    {
    }

    /// <summary>
    /// Scratch's clones: copies of an object made while the game runs, each running the same program. A clone gets
    /// its own copy of everything a <c>GameObject</c> carries, including its <c>ObjectProgramRunner</c>, so it
    /// starts its own scripts — and <c>when I start as a clone</c> is the hat that gives it something different to
    /// do from the object it came from.
    /// </summary>
    public sealed class BlockyClones
    {
        /// <summary>
        /// How many clones may exist at once. Scratch caps at 300; this is lower because each clone here is a real
        /// <c>GameObject</c> with colliders and a renderer. Past the cap, <c>create clone</c> quietly does nothing,
        /// which is what Scratch does too — a runaway <c>repeat forever { create clone }</c> should slow down, not
        /// take the machine with it.
        /// </summary>
        public const int MaxClones = 100;

        private readonly List<GameObject> _live = new();

        /// <summary>How many clones are alive right now (destroyed ones are dropped as they are found).</summary>
        public int Count
        {
            get
            {
                Prune();
                return _live.Count;
            }
        }

        /// <summary>
        /// Copies <paramref name="original"/> where it stands and hands back the copy, or null at the cap.
        ///
        /// The original is **not** deactivated around the copy, tempting as that is for keeping the clone's scripts
        /// quiet for an instant: disabling an object runs <c>ObjectProgramRunner.OnDisable</c>, which halts every
        /// thread it started — so an object cloning itself inside a loop would kill the very loop doing the
        /// cloning. Found in Play mode: `repeat 3 { create clone of me }` made exactly one clone.
        /// Nothing needs that window anyway: <c>Instantiate</c> returns before the scheduler ticks again, so the
        /// caller marks and announces the clone well before any of its scripts can run.
        /// </summary>
        public GameObject Create(GameObject original)
        {
            if (original == null || Count >= MaxClones) return null;

            var clone = Object.Instantiate(original, original.transform.position, original.transform.rotation, original.transform.parent);
            clone.name = original.name + " (clone)";
            if (clone.GetComponent<BlockyClone>() == null) clone.AddComponent<BlockyClone>();
            _live.Add(clone);

            return clone;
        }

        /// <summary>True when this object came from <see cref="Create"/> rather than from the scene.</summary>
        public static bool IsClone(GameObject go) => go != null && go.GetComponent<BlockyClone>() != null;

        /// <summary>Deletes one clone. Does nothing for an object that is not a clone — deleting the original is never what was meant.</summary>
        public bool Delete(GameObject go)
        {
            if (!IsClone(go)) return false;
            _live.Remove(go);
            Remove(go);
            return true;
        }

        /// <summary>Deletes every clone — what Stop does, so a run never leaves its copies lying around.</summary>
        public void DeleteAll()
        {
            foreach (var clone in _live)
                if (clone != null) Remove(clone);
            _live.Clear();
        }

        // Destroy is the right call while the game runs (it happens at the end of the frame, so nothing is pulled
        // out from under code that is still running), but outside play mode it logs an error and does nothing —
        // which is exactly where the tests live.
        private static void Remove(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        private void Prune() => _live.RemoveAll(go => go == null);
    }
}
