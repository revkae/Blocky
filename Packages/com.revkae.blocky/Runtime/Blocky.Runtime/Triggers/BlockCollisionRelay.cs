using System.Collections.Generic;
using UnityEngine;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime.Triggers
{
    /// <summary>
    /// Auto-added to targets with colliders, 3D or 2D; forwards to the broker, never runs blocks itself (TDD §6.7).
    /// It also keeps the set of objects this one is currently in contact with, so <c>touching?</c> is a set lookup
    /// rather than a physics query per condition per frame — a condition in a <c>repeat until</c> is evaluated every
    /// lap. No <c>RequireComponent(Collider)</c>: that would give a 2D sprite a 3D box it doesn't want.
    /// </summary>
    public sealed class BlockCollisionRelay : MonoBehaviour
    {
        // Domain reload is off in this project, so statics outlive a Play session — see ResetForNewPlaySession.
        private static readonly Dictionary<GameObject, BlockCollisionRelay> Relays = new();

        private readonly HashSet<GameObject> _contacts = new();
        private readonly List<GameObject> _destroyed = new(); // reused: pruning must not allocate per query

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession() => Relays.Clear();

        /// <summary>
        /// Whether <paramref name="target"/> is touching anything tagged <paramref name="tag"/> right now; an empty
        /// tag means "anything at all". False for an object with no relay — nothing has reported a contact for it.
        /// </summary>
        public static bool IsTouching(GameObject target, string tag)
        {
            if (target == null || !Relays.TryGetValue(target, out var relay) || relay == null) return false;
            return relay.HasContact(tag);
        }

        private void Awake() => Relays[gameObject] = this;

        private void OnDestroy()
        {
            if (Relays.TryGetValue(gameObject, out var relay) && relay == this) Relays.Remove(gameObject);
        }

        private void OnCollisionEnter(Collision collision) => Collided(collision.gameObject);

        private void OnCollisionExit(Collision collision) => _contacts.Remove(collision.gameObject);

        // Triggers count as touching too: a "touch me" zone is usually an IsTrigger collider, and Scratch's
        // touching? has no notion of a solid contact versus an overlap.
        private void OnTriggerEnter(Collider other) => _contacts.Add(other.gameObject);

        private void OnTriggerExit(Collider other) => _contacts.Remove(other.gameObject);

#if BLOCKY_PHYSICS2D // set by Blocky.Runtime.asmdef when the project has Unity's 2D physics module
        // The same four, for 2D physics — a separate engine with messages of its own.
        private void OnCollisionEnter2D(Collision2D collision) => Collided(collision.gameObject);

        private void OnCollisionExit2D(Collision2D collision) => _contacts.Remove(collision.gameObject);

        private void OnTriggerEnter2D(Collider2D other) => _contacts.Add(other.gameObject);

        private void OnTriggerExit2D(Collider2D other) => _contacts.Remove(other.gameObject);
#endif

        private void Collided(GameObject other)
        {
            _contacts.Add(other);
            BlockyRuntime.Triggers.RaiseCollided(gameObject, other);
        }

        /// <summary>Whether <paramref name="target"/> is in contact with <paramref name="other"/> specifically.</summary>
        public static bool IsTouchingObject(GameObject target, GameObject other)
        {
            if (target == null || other == null) return false;
            return Relays.TryGetValue(target, out var relay) && relay != null && relay._contacts.Contains(other);
        }

        private bool HasContact(string tag)
        {
            var wantAny = string.IsNullOrEmpty(tag);
            var found = false;

            foreach (var other in _contacts)
            {
                // A destroyed object never raises Exit, so its entry has to be dropped here.
                if (other == null) { _destroyed.Add(other); continue; }
                if (wantAny || other.CompareTag(tag)) { found = true; break; }
            }

            if (_destroyed.Count > 0)
            {
                foreach (var dead in _destroyed) _contacts.Remove(dead);
                _destroyed.Clear();
            }

            return found;
        }
    }
}
