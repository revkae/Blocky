using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Localization;
using UnityEngine.UIElements;

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
    /// </summary>
    public static class ParamFieldFactory
    {
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
                default: return value?.text ?? spec.defaultText ?? string.Empty;
            }
        }

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
