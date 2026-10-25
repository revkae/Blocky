using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Draws <see cref="VisualElement.tooltip"/> in a game. UI Toolkit shows tooltips only in Editor windows: in a
    /// runtime panel the text is set and nothing ever appears, so the workspace's buttons said nothing on hover. One
    /// of these, added to the workspace's root, shows the tooltip of whatever the pointer rests on inside that root —
    /// after <see cref="ShowDelay"/>, or at once while moving from one button to the next — under it, or beside it
    /// for an element in a <see cref="BesideClass"/> column. It answers only for its own parent's elements: a game's
    /// UI in the same panel may show its own tooltips. Add it after everything it explains (and before a
    /// <see cref="DragLayer"/>, which stays last: a drag puts the tooltip away anyway).
    /// It is driven, not event-based (<see cref="Tick"/>, from the workspace's frame): a disabled button gets no pointer
    /// events, and a disabled button — Step back before any step — is the one that most needs saying why.
    /// </summary>
    public sealed class TooltipLayer : VisualElement
    {
        /// <summary>Seconds the pointer rests on an element before its tooltip shows.</summary>
        public const float ShowDelay = 0.5f;

        /// <summary>Seconds after a tooltip goes in which the next one shows at once — reading along a bar of buttons.</summary>
        public const float WarmWindow = 0.4f;

        /// <summary>On an element or any of its ancestors: the tooltip goes beside it rather than under it — for a column of buttons along the right edge, which a tooltip underneath would cover.</summary>
        public const string BesideClass = "blocky-tooltip-beside";

        private const string ShownClass = "blocky-tooltip--shown";
        private const float Gap = 6f;        // between the element and its tooltip
        private const float EdgeMargin = 4f; // kept between the tooltip and its parent's edges

        private readonly Label _label;
        private VisualElement _owner;   // the element under the pointer whose tooltip this is (shown or waiting)
        private float _since;           // when the pointer came to it
        private bool _pressed;          // pressed since it came: no tooltip until the pointer leaves it
        private bool _shown;
        private bool _revealed;         // shown and placed: the frame after Show, once the layout has measured the text
        private float _hiddenAt = float.NegativeInfinity;

        public TooltipLayer()
        {
            AddToClassList("blocky-tooltip");
            pickingMode = PickingMode.Ignore; // never in the way of the pointer it follows
            style.position = Position.Absolute;
            style.display = DisplayStyle.None;

            _label = new Label { pickingMode = PickingMode.Ignore };
            _label.AddToClassList("blocky-tooltip__label");
            Add(_label);
        }

        /// <summary>Whether a tooltip is on show.</summary>
        public bool IsShown => _shown;

        /// <summary>The element whose tooltip is on show, or null.</summary>
        public VisualElement ShownFor => _shown ? _owner : null;

        /// <summary>What the tooltip on show says ("" with none).</summary>
        public string Text => _shown ? _label.text : string.Empty;

        /// <summary>
        /// One frame. <paramref name="hovered"/> is what the pointer is over (the panel's pick), or null when there is
        /// nothing to hover — a finger, which has no hover, or a drag under way; <paramref name="pressed"/> whether the
        /// pointer went down this frame (a press puts the tooltip away until the pointer moves to something else);
        /// <paramref name="now"/> the time, unscaled — a game that pauses time still has a pointer.
        /// </summary>
        public void Tick(VisualElement hovered, bool pressed, float now)
        {
            var owner = OwnerOf(hovered);
            if (owner != _owner)
            {
                if (_shown) Hide(now);
                _owner = owner;
                _since = now;
                _pressed = false;
            }

            if (owner == null) return;

            if (pressed)
            {
                _pressed = true;
                if (_shown) Hide(now);
            }

            if (_pressed) return;

            var text = owner.tooltip;
            if (!_shown)
            {
                if (now - _since < ShowDelay && now - _hiddenAt > WarmWindow) return;
                Show(text);
                return; // placed next frame, once the layout has measured the text
            }

            if (_label.text != text) _label.text = text; // the button changed what it does (Pause ↔ Resume)
            Place(owner);
            if (_revealed) return;

            _revealed = true;
            AddToClassList(ShownClass); // fades in, from where it now sits
        }

        /// <summary>Puts the tooltip away now — the workspace closed, or the game took the pointer.</summary>
        public void Hide() => Tick(null, false, Time.unscaledTime);

        /// <summary>The nearest element with a tooltip from <paramref name="hovered"/> up — a button's label is part of the button — as long as it is inside this layer's parent.</summary>
        private VisualElement OwnerOf(VisualElement hovered)
        {
            var scope = hierarchy.parent;
            VisualElement owner = null;
            for (var el = hovered; el != null; el = el.hierarchy.parent)
            {
                if (el == this) return null;
                if (el == scope) return owner;
                if (owner == null && !string.IsNullOrEmpty(el.tooltip)) owner = el;
            }
            return null; // somewhere else in the panel: not ours to explain
        }

        /// <summary>
        /// Shows the text, not yet placed and still see-through. It starts from the parent's top-left corner, so the
        /// layout measures it with the parent's whole width to wrap in; wherever it then goes, it fits there too.
        /// </summary>
        private void Show(string text)
        {
            _shown = true;
            _revealed = false;
            _label.text = text;
            style.left = 0f;
            style.top = 0f;
            style.display = DisplayStyle.Flex;
        }

        private void Hide(float now)
        {
            _shown = false;
            _revealed = false;
            _hiddenAt = now;
            RemoveFromClassList(ShownClass);
            style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Under the element, centered on it — above it when there is no room below — or, in a
        /// <see cref="BesideClass"/> column, to its left (its right when there is no room). Always inside the parent.
        /// </summary>
        private void Place(VisualElement owner)
        {
            var scope = hierarchy.parent;
            var size = layout.size;
            if (scope == null || float.IsNaN(size.x) || float.IsNaN(size.y)) return;

            var target = owner.worldBound;
            var area = scope.worldBound;
            if (float.IsNaN(target.x) || float.IsNaN(area.x)) return;

            Vector2 position;
            if (IsBeside(owner))
            {
                position = new Vector2(target.xMin - Gap - size.x, target.center.y - size.y / 2f);
                if (position.x < area.xMin + EdgeMargin) position.x = target.xMax + Gap;
            }
            else
            {
                position = new Vector2(target.center.x - size.x / 2f, target.yMax + Gap);
                if (position.y + size.y > area.yMax - EdgeMargin) position.y = target.yMin - Gap - size.y;
            }

            position.x = Mathf.Clamp(position.x, area.xMin + EdgeMargin, Mathf.Max(area.xMin + EdgeMargin, area.xMax - EdgeMargin - size.x));
            position.y = Mathf.Clamp(position.y, area.yMin + EdgeMargin, Mathf.Max(area.yMin + EdgeMargin, area.yMax - EdgeMargin - size.y));

            // left/top count from inside the parent's border.
            var local = scope.WorldToLocal(position) - new Vector2(scope.resolvedStyle.borderLeftWidth, scope.resolvedStyle.borderTopWidth);
            local = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y)); // whole pixels: text between two reads blurred
            if (style.left.value.value != local.x) style.left = local.x; // the inline value: resolvedStyle lags a layout pass behind
            if (style.top.value.value != local.y) style.top = local.y;
        }

        private static bool IsBeside(VisualElement owner)
        {
            for (var el = owner; el != null; el = el.hierarchy.parent)
                if (el.ClassListContains(BesideClass)) return true;
            return false;
        }
    }
}
