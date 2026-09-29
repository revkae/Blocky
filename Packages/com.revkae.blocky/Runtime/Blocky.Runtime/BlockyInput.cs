using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime
{
    /// <summary>
    /// The player's pointer, as condition blocks see it: the mouse, a pen, or the first finger on a touchscreen —
    /// whichever was used last (the Input System's <see cref="Pointer.current"/>), so a tablet taps where a laptop
    /// clicks. A press on the Blocky editor itself (dragging a block, clicking Go) is not a press in the game, so an
    /// on-screen editor reports where it is through <see cref="IsPointerOverUi"/> and those presses don't count as
    /// <c>mouse down?</c>.
    /// </summary>
    public static class BlockyInput
    {
        private static Func<bool> _buttonHeldOverride;

        /// <summary>Set by an on-screen editor: true while the pointer is over it. Null means "never over UI".</summary>
        public static Func<bool> IsPointerOverUi { get; set; }

        /// <summary>The main mouse button, a pen or a finger is pressed on the game (not on the editor).</summary>
        public static bool IsMouseDown => IsButtonHeld() && !(IsPointerOverUi?.Invoke() ?? false);

        /// <summary>Where the pointer is, in screen pixels (origin bottom-left, as the Input System gives it). False when there is no pointer at all.</summary>
        public static bool TryGetPointerPosition(out Vector2 screenPosition)
        {
            var pointer = Pointer.current;
            screenPosition = pointer != null ? pointer.position.ReadValue() : default;
            return pointer != null;
        }

        private static bool IsButtonHeld()
        {
            if (_buttonHeldOverride != null) return _buttonHeldOverride();
            var pointer = Pointer.current; // a Mouse's press is its left button; a Touchscreen's, its first finger
            return pointer != null && pointer.press.isPressed;
        }

        /// <summary>Test-only hook: replaces the real mouse button. Pass null to go back to the real mouse.</summary>
        public static void SetForTests(Func<bool> buttonHeld) => _buttonHeldOverride = buttonHeld;

        /// <summary>Domain reload is off (ADR-014): neither a test's mouse nor a closed session's editor may carry into the next Play session.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession()
        {
            _buttonHeldOverride = null;
            IsPointerOverUi = null;
        }
    }
}
