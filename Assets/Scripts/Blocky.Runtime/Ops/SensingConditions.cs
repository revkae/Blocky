using System;
using System.Collections.Generic;
using Blocky.Runtime.Triggers;
using UnityEngine.InputSystem;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>sensing.key_pressed</c> — true while that key is held, unlike <c>when key pressed</c>, which fires once on
    /// the press. This is what makes "hold to move" possible: a <c>repeat forever</c> asking every lap.
    /// </summary>
    [BlockExecutor("sensing.key_pressed")]
    public sealed class KeyPressedCondition : IConditionOp
    {
        // Parsing the name on every evaluation would re-parse the same handful of strings all frame. The names come
        // from a Choice param, so the set is small and fixed; a miss is cached as Key.None and never parsed again.
        private static readonly Dictionary<string, Key> KeysByName = new(StringComparer.OrdinalIgnoreCase);

        public bool Evaluate(ref SlotContext ctx)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;

            var name = ctx.GetText(0);
            if (string.IsNullOrEmpty(name)) return false;

            if (!KeysByName.TryGetValue(name, out var key))
            {
                if (!Enum.TryParse(name, true, out key)) key = Key.None;
                KeysByName[name] = key;
            }

            return key != Key.None && keyboard[key].isPressed;
        }
    }

    /// <summary>
    /// <c>sensing.touching</c> — true while this object is in contact with something tagged <c>tag</c> (an empty tag
    /// means anything). Answered from the contact set <see cref="BlockCollisionRelay"/> keeps, not from a fresh
    /// physics query, because a condition inside a loop is asked every lap.
    /// </summary>
    [BlockExecutor("sensing.touching")]
    public sealed class TouchingCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) => BlockCollisionRelay.IsTouching(ctx.Target, ctx.GetText(0));
    }

    /// <summary>
    /// <c>sensing.timer_above</c> — true once the shared timer passes <c>seconds</c>. The timer runs on the script
    /// clock, so pausing the scene pauses it too, and <c>reset timer</c> starts it again from zero.
    /// </summary>
    [BlockExecutor("sensing.timer_above")]
    public sealed class TimerAboveCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) => ctx.Timer > ctx.GetNumber(0);
    }
}
