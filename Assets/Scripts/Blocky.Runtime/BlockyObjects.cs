using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Turns the name in an <c>ObjectRef</c> input into a real <see cref="GameObject"/> — how one object's script
    /// talks about another ("point towards [Ball]", "distance to [Goal]"). Names are what this project already
    /// uses to identify an object (<c>ObjectProgram.targetObjectUid</c> is a name), so nothing new has to be
    /// stamped on the scene.
    ///
    /// Every programmed object registers itself, so the common case is a dictionary hit. Anything else is looked
    /// up once by name and remembered; a destroyed object's entry is dropped and looked up again. That matters
    /// because these blocks are read inside loops — a scene-wide search on every lap is exactly the thing this
    /// project's rules forbid.
    /// </summary>
    public sealed class BlockyObjects
    {
        /// <summary>Names that mean something other than an object in the scene.</summary>
        public const string CameraName = "camera";

        private static readonly string[] SelfNames = { "me", "myself", "self", "this" };

        private readonly Dictionary<string, GameObject> _byName = new(StringComparer.OrdinalIgnoreCase);

        // A name that matched nothing is remembered for the rest of the frame. Without this, one mistyped name in
        // a `repeat forever` would run a scene-wide search on every lap — the very thing the registry exists to
        // avoid. Per frame rather than forever, so an object that appears later is still found on the next frame.
        private readonly Dictionary<string, int> _missedOnFrame = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Called by every <c>ObjectProgramRunner</c> as it starts, so programmed objects never need a search.</summary>
        public void Register(GameObject go)
        {
            if (go == null) return;
            _byName[go.name] = go;
            _missedOnFrame.Remove(go.name); // it exists now, whatever an earlier lookup found
        }

        public void Unregister(GameObject go)
        {
            if (go != null && _byName.TryGetValue(go.name, out var known) && known == go) _byName.Remove(go.name);
        }

        /// <summary>
        /// The object <paramref name="name"/> refers to, or null. <paramref name="self"/> is the object running the
        /// script — what "me" means, and what an empty name falls back to.
        /// </summary>
        public GameObject Find(string name, GameObject self)
        {
            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0) return self;

            foreach (var selfName in SelfNames)
                if (string.Equals(key, selfName, StringComparison.OrdinalIgnoreCase)) return self;

            if (string.Equals(key, CameraName, StringComparison.OrdinalIgnoreCase))
                return Camera.main != null ? Camera.main.gameObject : null;

            if (_byName.TryGetValue(key, out var cached))
            {
                if (cached != null) return cached;
                _byName.Remove(key); // destroyed since; fall through and look again
            }

            if (_missedOnFrame.TryGetValue(key, out var frame) && frame == Time.frameCount) return null;

            var found = GameObject.Find(key);
            if (found != null)
            {
                _byName[key] = found;
                _missedOnFrame.Remove(key);
            }
            else
            {
                _missedOnFrame[key] = Time.frameCount;
            }

            return found;
        }

        /// <summary>Forgets every entry — a new play session starts from nothing.</summary>
        public void Clear()
        {
            _byName.Clear();
            _missedOnFrame.Clear();
        }
    }
}
