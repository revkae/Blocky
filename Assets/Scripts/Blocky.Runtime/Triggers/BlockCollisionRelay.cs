using UnityEngine;

namespace Blocky.Runtime.Triggers
{
    /// <summary>Auto-added to targets with colliders; forwards to the broker, never runs blocks itself (TDD §6.7).</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BlockCollisionRelay : MonoBehaviour
    {
        private void OnCollisionEnter(Collision collision) => BlockyRuntime.Triggers.RaiseCollided(gameObject, collision);
    }
}
