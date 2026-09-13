using System;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// The event ("hat") block that starts a stack: a rounded top with no notch — nothing can ever go above an
    /// event — and a tab underneath for the first block of the script. Fields are live with a store, static
    /// chips for a palette prototype, read-only otherwise.
    /// </summary>
    public sealed class HatView : VisualElement
    {
        public string StackId { get; }
        public VisualElement Header { get; }

        private readonly BlockShapePainter _painter;

        public HatView(BlockDefinition definition, string triggerBlockType, BlockParam[] values, string stackId, ProgramStore store, bool prototype = false)
        {
            StackId = stackId;
            values ??= Array.Empty<BlockParam>();

            AddToClassList("blocky-block");
            AddToClassList("blocky-shaped");
            AddToClassList("blocky-block--shape-trigger");
            if (definition != null) AddToClassList($"blocky-block--category-{definition.category.ToString().ToLowerInvariant()}");

            Header = new VisualElement();
            Header.AddToClassList("blocky-block__header");
            Header.Add(new Label(definition != null ? BlockView.DisplayName(definition) : $"Unknown trigger: {triggerBlockType}"));

            if (definition != null)
                foreach (var spec in definition.parameters)
                {
                    var value = Array.Find(values, p => p.key == spec.key);
                    if (prototype) Header.Add(ParamFieldFactory.CreateChip(spec, value));
                    else if (store != null)
                        Header.Add(ParamFieldFactory.CreateLiveField(spec, value, newValue =>
                            store.Apply(new SetParam(new ParamTarget(stackId, null), spec.key, newValue))));
                    else Header.Add(ParamFieldFactory.CreateReadOnlyField(spec, value));
                }

            Add(Header);

            _painter = new BlockShapePainter(this, () => new BlockOutline
            {
                Width = layout.width,
                Height = layout.height,
                Hat = true,
                BottomTab = true
            });
        }
    }
}
