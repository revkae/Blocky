using System;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Localization;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// The event ("hat") block that starts a stack: a rounded top with no notch — nothing can ever go above an
    /// event — and a tab underneath for the first block of the script. Fields are live with a store, static
    /// chips for a palette prototype, read-only otherwise.
    /// </summary>
    public sealed class HatView : VisualElement, IBlockElement
    {
        public string StackId { get; }

        /// <summary>Always null: a hat is addressed by its stack alone.</summary>
        public string NodeId => null;

        public VisualElement Header { get; }

        public HatView(BlockDefinition definition, string triggerBlockType, BlockParam[] values, string stackId, ProgramStore store, bool prototype = false)
        {
            StackId = stackId;
            values ??= Array.Empty<BlockParam>();

            AddToClassList("blocky-block");
            AddToClassList("blocky-shaped");
            AddToClassList("blocky-block--shape-trigger");
            if (definition != null) AddToClassList(BlockClasses.Category(definition.category));

            Header = new VisualElement();
            Header.AddToClassList("blocky-block__header");
            Header.Add(new Label(definition != null ? BlockView.DisplayName(definition) : BlockyText.Format("block.unknown_trigger", triggerBlockType)));

            // A null node id targets the stack's trigger params. No registry: triggers have no condition slots.
            if (definition != null)
                foreach (var spec in definition.parameters)
                    Header.Add(BlockParams.Create(spec, Array.Find(values, p => p.key == spec.key), definition, null, null, stackId, store, prototype));

            Add(Header);

            new BlockShapePainter(this, () => new BlockOutline
            {
                Width = layout.width,
                Height = layout.height,
                Hat = true,
                BottomTab = true
            });
        }
    }
}
