using System;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// A condition block (<see cref="BlockShape.Boolean"/>): Scratch's hexagon. Pointed ends, no notch, no tab —
    /// the shape says "this doesn't stack, it goes *inside* something", and the only place it fits is a
    /// <see cref="ConditionSlot"/> of the same shape. It sits either in a slot (<see cref="OwnerNodeId"/> and
    /// <see cref="ParamKey"/> name it) or loose on the table, as the single block of a loose stack.
    /// </summary>
    public sealed class ConditionView : VisualElement, IBlockElement
    {
        public string NodeId { get; }
        public string StackId { get; }
        public BlockDefinition Definition { get; }

        /// <summary>The block whose slot holds this condition; null when it's lying loose on the table.</summary>
        public string OwnerNodeId { get; }

        /// <summary>Which of the owner's params the slot is; null when loose.</summary>
        public string ParamKey { get; }

        public bool IsInSlot => OwnerNodeId != null;

        /// <summary>A palette entry: the definition with its default values, as static chips.</summary>
        public static ConditionView CreatePrototype(BlockDefinition definition, BlockRegistry registry) =>
            new(PaletteView.InstantiatePrototype(definition), definition, registry, null, null, prototype: true);

        public ConditionView(BlockNode node, BlockDefinition definition, BlockRegistry registry, string stackId, ProgramStore store,
            string ownerNodeId = null, string paramKey = null, bool prototype = false)
        {
            NodeId = node.id;
            StackId = stackId;
            Definition = definition;
            OwnerNodeId = ownerNodeId;
            ParamKey = paramKey;

            AddToClassList("blocky-block");
            AddToClassList("blocky-shaped");
            AddToClassList(BlockClasses.Category(definition.category));
            AddToClassList("blocky-block--shape-boolean");

            var header = new VisualElement();
            header.AddToClassList("blocky-block__header");
            header.Add(new Label(BlockView.DisplayName(definition)));
            foreach (var spec in definition.parameters)
                header.Add(BlockParams.Create(spec, Array.Find(node.parameters, p => p.key == spec.key), definition, node.id, registry, stackId, store, prototype));
            Add(header);

            new BlockShapePainter(this, () => new BlockOutline { Width = layout.width, Height = layout.height, Hexagon = true });
        }
    }

    /// <summary>
    /// A block's condition input (a <see cref="ParamKind.Reporter"/> param): a hexagonal hole, drawn in a darker
    /// shade of its block, that a <see cref="ConditionView"/> drops into. Clicking the empty hole lists every
    /// condition block in a dropdown; picking one puts it there. Filled, it's just the condition inside it —
    /// drag that out, or select it and press Delete, to empty the slot again.
    /// </summary>
    public sealed class ConditionSlot : VisualElement
    {
        public const string UssClassName = "blocky-condition-slot";
        private const float EmptyShade = 0.72f;

        public string StackId { get; }
        public string OwnerNodeId { get; }
        public string ParamKey { get; }

        /// <summary>Nothing in the slot right now — including while its condition is being dragged out.</summary>
        public bool IsEmpty => childCount == 0;

        private readonly BlockRegistry _registry;
        private readonly ProgramStore _store;

        public ConditionSlot(ParamSpec spec, BlockParam value, BlockDefinition ownerDefinition, string ownerNodeId, BlockRegistry registry,
            string stackId, ProgramStore store, bool prototype)
        {
            StackId = stackId;
            OwnerNodeId = ownerNodeId;
            ParamKey = spec.key;
            _registry = registry;
            _store = prototype ? null : store;

            AddToClassList("blocky-param-field");
            AddToClassList(UssClassName);
            // The owner's category class gives the hole its block's color (darkened below) through --blocky-fill.
            AddToClassList(BlockClasses.Category(ownerDefinition.category));

            var condition = !prototype && value?.kind == ParamKind.Reporter ? value.reporter : null;
            if (condition != null)
            {
                var definition = registry.Find(condition.blockType);
                Add(definition != null
                    ? new ConditionView(condition, definition, registry, stackId, store, ownerNodeId, spec.key)
                    : new UnknownBlockView(condition));
            }

            // Only the empty hole is drawn; a filled slot is the condition's own hexagon.
            new BlockShapePainter(this, () => IsEmpty
                ? new BlockOutline { Width = layout.width, Height = layout.height, Hexagon = true }
                : default, EmptyShade);

            if (_store != null) RegisterCallback<ClickEvent>(OnClick);
        }

        private void OnClick(ClickEvent evt)
        {
            if (!IsEmpty || evt.button != 0) return;

            var menu = new GenericDropdownMenu();
            for (var opcode = 0; opcode < _registry.Count; opcode++)
            {
                var definition = _registry.GetByOpcode(opcode);
                if (definition.shape == BlockShape.Boolean)
                    menu.AddItem(BlockView.DisplayName(definition), false, () => Fill(definition));
            }

            menu.DropDown(worldBound, this, DropdownMenuSizeMode.Content);
            evt.StopPropagation();
        }

        private void Fill(BlockDefinition definition) =>
            _store.Apply(new SetParam(new ParamTarget(StackId, OwnerNodeId), ParamKey, new BlockParam
            {
                key = ParamKey,
                kind = ParamKind.Reporter,
                reporter = PaletteView.InstantiatePrototype(definition)
            }));
    }

    /// <summary>One param of a block's header: a condition slot for a <see cref="ParamKind.Reporter"/>, otherwise a <see cref="ParamFieldFactory"/> control.</summary>
    internal static class BlockParams
    {
        public static VisualElement Create(ParamSpec spec, BlockParam value, BlockDefinition ownerDefinition, string nodeId,
            BlockRegistry registry, string stackId, ProgramStore store, bool prototype)
        {
            if (spec.kind == ParamKind.Reporter)
                return new ConditionSlot(spec, value, ownerDefinition, nodeId, registry, stackId, store, prototype);
            if (prototype) return ParamFieldFactory.CreateChip(spec, value);
            if (store != null)
                return ParamFieldFactory.CreateLiveField(spec, value, newValue =>
                    store.Apply(new SetParam(new ParamTarget(stackId, nodeId), spec.key, newValue)));
            return ParamFieldFactory.CreateReadOnlyField(spec, value);
        }
    }
}
