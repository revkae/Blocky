using System;
using UnityEngine.InputSystem;

namespace Blocky.Runtime
{
    /// <summary>
    /// The player's pointer, as condition blocks see it. A press on the Blocky editor itself (dragging a block,
    /// clicking Go) is not a press in the game, so an on-screen editor reports where it is through
    /// <see cref="IsPointerOverUi"/> and those presses don't count as <c>mouse down?</c>.
    /// </summary>
    public static class BlockyInput
    {
        private static Func<bool> _buttonHeldOverride;

        /// <summary>Set by an on-screen editor: true while the pointer is over it. Null means "never over UI".</summary>
        public static Func<bool> IsPointerOverUi { get; set; }

        /// <summary>The main (left) mouse button is held over the game.</summary>
        public static bool IsMouseDown => IsButtonHeld() && !(IsPointerOverUi?.Invoke() ?? false);

        private static bool IsButtonHeld()
        {
            if (_buttonHeldOverride != null) return _buttonHeldOverride();
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
        }

        /// <summary>Test-only hook: replaces the real mouse button. Pass null to go back to the real mouse.</summary>
        public static void SetForTests(Func<bool> buttonHeld) => _buttonHeldOverride = buttonHeld;
    }
}
