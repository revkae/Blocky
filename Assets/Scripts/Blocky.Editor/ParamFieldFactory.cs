using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Maps a <see cref="ParamKind"/> to a control. Adding a param kind touches only this file (TDD §8.2).
    /// <see cref="CreateReadOnlyField"/> is Milestone 4's static preview (every field disabled);
    /// <see cref="CreateLiveField"/> wires the same control types live, dispatching a new <see cref="BlockParam"/>
    /// through a callback on every edit rather than through the UI Toolkit binding system, so the caller decides
    /// how to turn that into a <c>SetParam</c> command.
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
            return field;
        }

        private static VisualElement BuildField(ParamSpec spec, BlockParam value, Action<BlockParam> onChanged)
        {
            switch (spec.kind)
            {
                case ParamKind.Number:
                {
                    var field = new FloatField(spec.key) { value = value?.number ?? spec.defaultNumber };
                    if (onChanged != null)
                        field.RegisterValueChangedCallback(evt => onChanged(new BlockParam { key = spec.key, kind = ParamKind.Number, number = evt.newValue }));
                    return field;
                }
                case ParamKind.Bool:
                {
                    var field = new Toggle(spec.key) { value = value?.boolean ?? false };
                    if (onChanged != null)
                        field.RegisterValueChangedCallback(evt => onChanged(new BlockParam { key = spec.key, kind = ParamKind.Bool, boolean = evt.newValue }));
                    return field;
                }
                case ParamKind.Text:
                case ParamKind.ObjectRef:
                {
                    var field = new TextField(spec.key) { value = value?.text ?? spec.defaultText };
                    if (onChanged != null)
                        field.RegisterValueChangedCallback(evt => onChanged(new BlockParam { key = spec.key, kind = spec.kind, text = evt.newValue }));
                    return field;
                }
                case ParamKind.Choice:
                {
                    var labels = new List<string>(spec.choices.Length);
                    foreach (var choice in spec.choices) labels.Add(choice.stableId);
                    var selectedIndex = value != null ? Array.FindIndex(spec.choices, c => c.stableId == value.text) : -1;
                    var field = new DropdownField(spec.key, labels, Math.Max(selectedIndex, 0));
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
