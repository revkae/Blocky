using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Localization;
using UnityEngine.UIElements;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Editor
{
    /// <summary>
    /// Maps a <see cref="ParamKind"/> to a control. Adding a param kind touches only this file (TDD §8.2).
    /// <see cref="CreateReadOnlyField"/> is Milestone 4's static preview (every field disabled);
    /// <see cref="CreateLiveField"/> wires the same control types live, dispatching a new <see cref="BlockParam"/>
    /// through a callback on every edit rather than through the UI Toolkit binding system, so the caller decides
    /// how to turn that into a <c>SetParam</c> command.
    /// Labels and choice names are in the player's language (<see cref="ParamSpec.DisplayName"/>,
    /// <see cref="ParamSpec.ChoiceDisplayName"/>); what gets stored is always the stable key and choice id.
    /// A game can shape two things from outside (ADR-041): <see cref="ObjectChoices"/> turns object inputs into a
    /// dropdown of its objects, and <see cref="IsHidden"/> takes an input off a block.
    /// </summary>
    public static class ParamFieldFactory
    {
        /// <summary>What an object input stores for "the object running the script" — one of the names the runtime reads as self.</summary>
        public const string MeValue = "me";

        private static readonly string[] SelfNames = { MeValue, "myself", "self", "this" }; // as BlockyObjects reads them

        /// <summary>
        /// The names offered for an object input, in order — set by the game, since this assembly can't see the scene.
        /// Null: a free text field, as it always was. Asked each time a field is built. "me" is always offered first,
        /// and the program still stores the name: one the list no longer has is shown anyway, so an old program never
        /// loses a value.
        /// </summary>
        public static Func<IReadOnlyList<string>> ObjectChoices { get; set; }

        /// <summary>
        /// Asked for each input as a block is drawn, with the block's type and the input's key: true leaves it off the
        /// block. Its value stays in the program. For an input that does nothing in this game — the angle on
        /// "when looked at" when the game decides what is looked at. Null: every input shows.
        /// </summary>
        public static Func<string, string, bool> IsHidden { get; set; }

        internal static bool Hides(BlockDefinition definition, ParamSpec spec) =>
            IsHidden != null && definition != null && IsHidden(definition.blockType, spec.key);

        /// <summary>Domain reload is off (ADR-014): a game's hooks would otherwise still be here, with its objects gone, in the next Play session.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession()
        {
            ObjectChoices = null;
            IsHidden = null;
        }

        public static VisualElement CreateReadOnlyField(ParamSpec spec, BlockParam value)
        {
            var field = BuildField(spec, value, null);
            field.SetEnabled(false);
            field.AddToClassList("blocky-param-field");
            return field;
        }

        public static VisualElement CreateLiveField(ParamSpec spec, BlockParam value, Action<BlockParam> onChanged)
        {
            var field = BuildField(spec, value, onChanged);
            field.AddToClassList("blocky-param-field");

            // The key label belongs to the block's drag handle, not to the control. Left pickable, a number field's
            // label is Unity's "drag to change the value" handle, which fights dragging the block.
            var label = field.Q(className: "unity-base-field__label");
            if (label != null) label.pickingMode = PickingMode.Ignore;

            // A Toggle is clickable across its whole row, label included. Only the checkbox square should tick it —
            // a press on the text is a press on the block (select / drag). The click handler lives on the Toggle
            // itself, so clicks on the square still reach it by bubbling.
            if (field is Toggle) field.pickingMode = PickingMode.Ignore;

            // Commit on Enter / focus loss, not per keystroke: every committed value rebuilds the canvas, which
            // would destroy the very field being typed into after the first character.
            if (field is FloatField floatField) floatField.isDelayed = true;
            if (field is TextField textField) textField.isDelayed = true;

            return field;
        }

        /// <summary>
        /// Static display for palette prototypes: the key, then the value in a white oval. No input control — the
        /// whole prototype is one drag handle, so nothing inside it may take the pointer.
        /// </summary>
        public static VisualElement CreateChip(ParamSpec spec, BlockParam value)
        {
            var row = new VisualElement();
            row.AddToClassList("blocky-param-field");

            var key = new Label(spec.DisplayName);
            key.AddToClassList("blocky-param-chip__key");
            row.Add(key);

            var chip = new Label(DescribeValue(spec, value));
            chip.AddToClassList("blocky-param-chip");
            row.Add(chip);
            return row;
        }

        private static string DescribeValue(ParamSpec spec, BlockParam value)
        {
            switch (spec.kind)
            {
                case ParamKind.Number: return (value?.number ?? spec.defaultNumber).ToString("0.##");
                case ParamKind.Bool: return BlockyText.Get((value?.boolean ?? false) ? "value.true" : "value.false");
                case ParamKind.Choice: return spec.ChoiceDisplayName(value?.text ?? spec.defaultText ?? string.Empty);
                case ParamKind.ObjectRef when ObjectChoices != null: return ObjectDisplayName(ObjectValue(value?.text ?? spec.defaultText));
                default: return value?.text ?? spec.defaultText ?? string.Empty;
            }
        }

        /// <summary>
        /// An object input as a dropdown: "me", then the game's names, then the stored name if neither has it. Names
        /// are matched the way the runtime finds objects — trimmed, whatever the capitals.
        /// </summary>
        private static VisualElement BuildObjectDropdown(ParamSpec spec, BlockParam value, Action<BlockParam> onChanged)
        {
            var names = new List<string> { MeValue };
            var offered = ObjectChoices();
            if (offered != null)
                foreach (var name in offered)
                    if (!string.IsNullOrWhiteSpace(name) && IndexOfName(names, ObjectValue(name)) < 0) names.Add(name.Trim());

            var stored = ObjectValue(value?.text ?? spec.defaultText);
            var selected = IndexOfName(names, stored);
            if (selected < 0)
            {
                names.Add(stored);
                selected = names.Count - 1;
            }

            var field = new DropdownField(spec.DisplayName, names, selected, ObjectDisplayName, ObjectDisplayName);
            if (onChanged != null)
                field.RegisterValueChangedCallback(evt => onChanged(new BlockParam { key = spec.key, kind = ParamKind.ObjectRef, text = evt.newValue }));
            return field;
        }

        /// <summary>The name as the dropdown lists it: every way of saying "the object running the script" (an empty input included) is "me".</summary>
        private static string ObjectValue(string name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) return MeValue;
            foreach (var self in SelfNames)
                if (string.Equals(trimmed, self, StringComparison.OrdinalIgnoreCase)) return MeValue;
            return trimmed;
        }

        private static int IndexOfName(List<string> names, string name) =>
            names.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        private static string ObjectDisplayName(string name) => name == MeValue ? BlockyText.Get("object.me", MeValue) : name;

        private static VisualElement BuildField(ParamSpec spec, BlockParam value, Action<BlockParam> onChanged)
        {
            switch (spec.kind)
            {
                case ParamKind.Number:
                {
                    var field = new FloatField(spec.DisplayName) { value = value?.number ?? spec.defaultNumber };
                    if (onChanged != null)
                        field.RegisterValueChangedCallback(evt => onChanged(new BlockParam { key = spec.key, kind = ParamKind.Number, number = evt.newValue }));
                    return field;
                }
                case ParamKind.Bool:
                {
                    var field = new Toggle(spec.DisplayName) { value = value?.boolean ?? false };
                    if (onChanged != null)
                        field.RegisterValueChangedCallback(evt => onChanged(new BlockParam { key = spec.key, kind = ParamKind.Bool, boolean = evt.newValue }));
                    return field;
                }
                case ParamKind.ObjectRef when ObjectChoices != null:
                    return BuildObjectDropdown(spec, value, onChanged);
                case ParamKind.Text:
                case ParamKind.ObjectRef:
                {
                    var field = new TextField(spec.DisplayName) { value = value?.text ?? spec.defaultText };
                    if (onChanged != null)
                        field.RegisterValueChangedCallback(evt => onChanged(new BlockParam { key = spec.key, kind = spec.kind, text = evt.newValue }));
                    return field;
                }
                case ParamKind.Choice:
                {
                    // The choices are the stable ids — they are what the program stores — shown by their names.
                    var ids = new List<string>(spec.choices.Length);
                    foreach (var choice in spec.choices) ids.Add(choice.stableId);
                    var selectedIndex = value != null ? Array.FindIndex(spec.choices, c => c.stableId == value.text) : -1;
                    var field = new DropdownField(spec.DisplayName, ids, Math.Max(selectedIndex, 0), spec.ChoiceDisplayName, spec.ChoiceDisplayName);
                    if (onChanged != null)
                        field.RegisterValueChangedCallback(evt => onChanged(new BlockParam { key = spec.key, kind = ParamKind.Choice, text = evt.newValue }));
                    return field;
                }
                default: // Reporter — unimplemented in v1, TDD §1.2
                    return new Label($"{spec.key}: (expression — unimplemented in v1)");
            }
        }
    }
}
