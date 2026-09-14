using System;
using Blocky.Compiler;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// A simple inline list of block prototypes to pick from, filtered by a predicate. Deliberately not
    /// <c>UnityEditor.GenericMenu</c> — this stays usable at runtime too, not just in editor tooling.
    /// </summary>
    public sealed class BlockPickerPopup : VisualElement
    {
        public BlockPickerPopup(BlockRegistry registry, Func<BlockDefinition, bool> filter, Action<BlockDefinition> onPick)
        {
            AddToClassList("blocky-picker-popup");
            style.display = DisplayStyle.None;

            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var def = registry.GetByOpcode(opcode);
                if (!filter(def)) continue;

                var item = new Label(BlockView.DisplayName(def));
                item.AddToClassList("blocky-picker-popup__item");
                item.RegisterCallback<PointerUpEvent>(_ =>
                {
                    style.display = DisplayStyle.None;
                    onPick(def);
                });
                Add(item);
            }
        }

        public void Toggle() => style.display = style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
