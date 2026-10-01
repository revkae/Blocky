using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Marks the renderers of a <see cref="BlockySpeechBubble"/>. A bubble hangs under the object that speaks, so
    /// without this "change color" would tint the bubble along with the object, and Reset would track it as part of
    /// the object's look.
    /// </summary>
    [AddComponentMenu("")] // made by the bubble, never added by hand
    internal sealed class BlockyBubblePart : MonoBehaviour
    {
    }
}
