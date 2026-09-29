using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Single absolutely-positioned overlay, sibling of the canvas, that holds the dragged ghost (and the snap glow)
    /// while a <see cref="ChainDragSession"/> is in progress (TDD §8.2). Always the last sibling of its parent so the
    /// dragged block renders above everything and survives a canvas rebuild (TDD §8.4) — callers must add it last
    /// and call <c>BringToFront()</c> after any sibling is added afterward.
    /// </summary>
    public sealed class DragLayer : VisualElement
    {
        public DragLayer()
        {
            AddToClassList("blocky-drag-layer");
            pickingMode = PickingMode.Ignore; // never itself a drop target or event target
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
        }
    }
}
