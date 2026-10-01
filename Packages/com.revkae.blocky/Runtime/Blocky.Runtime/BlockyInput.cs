using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime
{
    /// <summary>
    /// The player's pointer, as condition blocks see it: the mouse, a pen, or the first finger on a touchscreen —
    /// whichever was used last (the Input System's <see cref="Pointer.current"/>), so a tablet taps where a laptop
    /// clicks. A press on the Blocky editor itself (dragging a block, clicking Go) is not a press in the game, and
    /// neither is one on the game's own interface: whatever is drawn over the game says where it is through
    /// <see cref="AddPointerOverUiCheck"/>, and those presses don't count as <c>mouse down?</c> or "when clicked".
    /// </summary>
    public static class BlockyInput
    {
        private static Func<bool> _buttonHeldOverride;
        private static readonly List<Func<bool>> OverUiChecks = new();

        /// <summary>
        /// One check a game can set: true while the pointer is over its interface. Null means "never over UI". Kept
        /// for code written for 0.1.0; <see cref="AddPointerOverUiCheck"/> lets several interfaces each say so.
        /// </summary>
        public static Func<bool> IsPointerOverUi { get; set; }

        /// <summary>
        /// Adds a check that answers true while the pointer is over something drawn on top of the game — the Blocky
        /// workspace adds its own, and a game adds one for its own interface (say, uGUI's
        /// <c>EventSystem.current.IsPointerOverGameObject()</c>). While any says true, <see cref="IsMouseDown"/> is
        /// false. Asked whenever a block or the click poll reads the pointer, so keep it cheap. Dropped at the start
        /// of every Play session: add it again as the game starts.
        /// </summary>
        public static void AddPointerOverUiCheck(Func<bool> isOver)
        {
            if (isOver != null && !OverUiChecks.Contains(isOver)) OverUiChecks.Add(isOver);
        }

        public static void RemovePointerOverUiCheck(Func<bool> isOver) => OverUiChecks.Remove(isOver);

        /// <summary>The main mouse button, a pen or a finger is pressed on the game (not on the editor, nor on the game's own interface).</summary>
        public static bool IsMouseDown => IsButtonHeld() && !IsOverAnyUi();

        /// <summary>Where the pointer is, in screen pixels (origin bottom-left, as the Input System gives it). False when there is no pointer at all.</summary>
        public static bool TryGetPointerPosition(out Vector2 screenPosition)
        {
            var pointer = Pointer.current;
            screenPosition = pointer != null ? pointer.position.ReadValue() : default;
            return pointer != null;
        }

        private static bool IsOverAnyUi()
        {
            if (IsPointerOverUi?.Invoke() ?? false) return true;
            for (var i = 0; i < OverUiChecks.Count; i++)
                if (OverUiChecks[i]()) return true;
            return false;
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
            OverUiChecks.Clear();
        }
    }
}
