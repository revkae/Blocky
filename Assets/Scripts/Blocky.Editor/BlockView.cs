using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// One VisualElement subtree per node (TDD §8.2), drawn with its Scratch-style silhouette
    /// (<see cref="BlockOutline"/>): header row, then per branch a mouth (<see cref="BodySlots"/>) with an arm
    /// between branches ("else") and a footer arm after the last. Holds the node's <see cref="NodeId"/>, never
    /// the <see cref="BlockNode"/> itself, so a rebuilt data model never leaves a stale pointer.
    /// Field modes: read-only (no store), live (store — edits dispatch <see cref="SetParam"/>), prototype
    /// (palette — static chips). <c>buttons</c> adds the Editor window's delete/"+ Add" affordances; the in-game
    /// table passes false and uses drag and drop instead.
    /// </summary>
    public sealed class BlockView : VisualElement, IBlockElement
    {
        public string NodeId { get; }
        public string StackId { get; }
        public BlockDefinition Definition { get; }

        /// <summary>A cap block (e.g. <c>forever</c>): no tab underneath, so nothing can attach below it.</summary>
        public bool IsTerminal => Definition.shape == BlockShape.Cap;

        private readonly List<VisualElement> _bodySlots = new();

        /// <summary>One entry per branch (TDD §8.2's <c>bodySlot</c>), in branch order.</summary>
        public IReadOnlyList<VisualElement> BodySlots => _bodySlots;

        /// <summary>
        /// Builds a normal block view, a <see cref="ConditionView"/> for a condition lying loose on the table, or an
        /// <see cref="UnknownBlockView"/> if the block type no longer resolves.
        /// </summary>
        public static VisualElement Create(BlockNode node, BlockRegistry registry, string stackId = null, ProgramStore store = null, bool buttons = true)
        {
            var definition = registry.Find(node.blockType);
            if (definition == null) return new UnknownBlockView(node);
            return definition.shape == BlockShape.Boolean
                ? new ConditionView(node, definition, registry, stackId, store)
                : new BlockView(node, definition, registry, stackId, store, buttons, prototype: false);
        }

        /// <summary>What the Editor window's "+ Add" pickers offer inside a sequence: no hats, no conditions.</summary>
        internal static bool IsSequenceBlock(BlockDefinition definition) =>
            definition.shape != BlockShape.Trigger && definition.shape != BlockShape.Boolean;

        /// <summary>A palette entry: the definition with its default values, as static chips.</summary>
        public static BlockView CreatePrototype(BlockDefinition definition, BlockRegistry registry) =>
            new(PaletteView.InstantiatePrototype(definition), definition, registry, null, null, buttons: false, prototype: true);

        internal static string DisplayName(BlockDefinition definition) => definition.DisplayName;

        private BlockView(BlockNode node, BlockDefinition definition, BlockRegistry registry, string stackId, ProgramStore store, bool buttons, bool prototype)
        {
            NodeId = node.id;
            StackId = stackId;
            Definition = definition;

            AddToClassList("blocky-block");
            AddToClassList("blocky-shaped");
            AddToClassList(BlockClasses.Category(definition.category));
            AddToClassList($"blocky-block--shape-{definition.shape.ToString().ToLowerInvariant()}");

            var header = new VisualElement();
            header.AddToClassList("blocky-block__header");
            header.Add(new Label(DisplayName(definition)));

            foreach (var spec in definition.parameters)
                header.Add(BlockParams.Create(spec, Array.Find(node.parameters, p => p.key == spec.key), definition, node.id, registry, stackId, store, prototype));

            if (store != null && buttons)
            {
                var deleteButton = new Button(() => store.Apply(new DeleteBlock(stackId, node.id))) { text = "✕" };
                deleteButton.AddToClassList("blocky-block__delete");
                header.Add(deleteButton);
            }

            Add(header);

            for (var branchIndex = 0; branchIndex < definition.branchCount; branchIndex++)
            {
                if (branchIndex > 0) Add(BuildArm(definition.branchCount == 2 ? "else" : null, footer: false));

                var slot = new VisualElement();
                slot.AddToClassList("blocky-block__body-slot");

                if (branchIndex < node.branches.Length)
                    foreach (var child in node.branches[branchIndex])
                        slot.Add(Create(child, registry, stackId, store, buttons));

                if (store != null && buttons)
                    slot.Add(BuildAddButton(registry, store, stackId, node.id, branchIndex));

                _bodySlots.Add(slot);
                Add(slot);
            }

            if (definition.branchCount > 0) Add(BuildArm(null, footer: true));

            var painter = new BlockShapePainter(this, BuildOutline); // kept alive by the callbacks it registers on this element
            foreach (var slot in _bodySlots) painter.TrackGeometryOf(slot);
        }

        private BlockOutline BuildOutline()
        {
            var mouths = new List<Vector2>(_bodySlots.Count);
            foreach (var slot in _bodySlots) mouths.Add(new Vector2(slot.layout.y, slot.layout.yMax));
            return new BlockOutline { Width = layout.width, Height = layout.height, BottomTab = !IsTerminal, Mouths = mouths };
        }

        private static VisualElement BuildArm(string label, bool footer)
        {
            var arm = new VisualElement();
            arm.AddToClassList(footer ? "blocky-block__footer" : "blocky-block__arm");
            if (label != null) arm.Add(new Label(label));
            return arm;
        }

        private static VisualElement BuildAddButton(BlockRegistry registry, ProgramStore store, string stackId, string parentNodeId, int branchIndex)
        {
            var container = new VisualElement();
            container.AddToClassList("blocky-block__add-container");

            var popup = new BlockPickerPopup(registry, IsSequenceBlock, def =>
            {
                var parent = ProgramQuery.FindNode(store.Program, stackId, parentNodeId);
                var index = parent?.branches[branchIndex].Length ?? 0;
                var location = NodeLocation.InBranch(stackId, parentNodeId, branchIndex, index);
                store.Apply(new InsertNode(location, PaletteView.InstantiatePrototype(def)));
            });

            var addButton = new Button(popup.Toggle) { text = "+ Add" };
            addButton.AddToClassList("blocky-block__add-button");

            container.Add(addButton);
            container.Add(popup);
            return container;
        }
    }
}
