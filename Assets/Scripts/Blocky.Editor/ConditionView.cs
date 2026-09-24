using System;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// A block that lives inside another block's input rather than in a sequence: a condition
    /// (<see cref="BlockShape.Boolean"/>, Scratch's hexagon) or a reporter (<see cref="BlockShape.Reporter"/>,
    /// Scratch's round block). Pointed or rounded ends, no notch and no tab — the shape says "this doesn't stack,
    /// it goes *inside* something". It sits either in a <see cref="ConditionSlot"/> (<see cref="OwnerNodeId"/> and
    /// <see cref="ParamKey"/> name it) or loose on the table, as the single block of a loose stack.
    /// </summary>
    public sealed class ConditionView : VisualElement, IBlockElement
    {
        public string NodeId { get; }
        public string StackId { get; }
        public BlockDefinition Definition { get; }

        /// <summary>The block whose input holds this one; null when it's lying loose on the table.</summary>
        public string OwnerNodeId { get; }

        /// <summary>Which of the owner's params the slot is; null when loose.</summary>
        public string ParamKey { get; }

        public bool IsInSlot => OwnerNodeId != null;

        /// <summary>True for a hexagon (a condition), false for a round reporter.</summary>
        public bool IsCondition => Definition.shape == BlockShape.Boolean;

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

            var hexagon = definition.shape == BlockShape.Boolean;

            AddToClassList("blocky-block");
            AddToClassList("blocky-shaped");
            AddToClassList(BlockClasses.Category(definition.category));
            AddToClassList(hexagon ? "blocky-block--shape-boolean" : "blocky-block--shape-reporter");

            var header = new VisualElement();
            header.AddToClassList("blocky-block__header");
            header.Add(new Label(BlockView.DisplayName(definition)));
            foreach (var spec in definition.parameters)
                header.Add(BlockParams.Create(spec, Array.Find(node.parameters, p => p.key == spec.key), definition, node.id, registry, stackId, store, prototype));
            Add(header);

            new BlockShapePainter(this, () => new BlockOutline { Width = layout.width, Height = layout.height, Hexagon = hexagon, Pill = !hexagon });
        }
    }

    /// <summary>
    /// A block's input: either a hexagonal hole for a condition (a <see cref="ParamKind.Reporter"/> param) or the
    /// white oval of an ordinary value param, which a reporter can be dropped into just as well — that is what
    /// makes <c>move forward (pick random 1 to 10)</c> possible without a single block knowing about it.
    /// Empty, a value slot shows its normal control so the value can still be typed; empty, a condition hole is
    /// drawn as a hexagon and clicking it lists every condition. Filled, both are just the block inside them —
    /// drag it out, or select it and press Delete, to empty the slot again.
    /// </summary>
    public sealed class ConditionSlot : VisualElement
    {
        public const string UssClassName = "blocky-condition-slot";
        public const string ValueUssClassName = "blocky-value-slot";
        private const float EmptyShade = 0.72f;

        public string StackId { get; }
        public string OwnerNodeId { get; }
        public string ParamKey { get; }

        /// <summary>A hexagonal condition hole (takes only conditions) rather than a value input (takes reporters too).</summary>
        public bool IsConditionHole { get; }

        /// <summary>Nothing in the slot right now — including while its block is being dragged out.</summary>
        public bool IsEmpty => _block == null || _block.parent != this;

        private readonly BlockRegistry _registry;
        private readonly ProgramStore _store;
        private readonly VisualElement _block; // the condition or reporter in the slot, if any

        public ConditionSlot(ParamSpec spec, BlockParam value, BlockDefinition ownerDefinition, string ownerNodeId, BlockRegistry registry,
            string stackId, ProgramStore store, bool prototype)
        {
            StackId = stackId;
            OwnerNodeId = ownerNodeId;
            ParamKey = spec.key;
            IsConditionHole = spec.kind == ParamKind.Reporter;
            _registry = registry;
            _store = prototype ? null : store;

            if (IsConditionHole)
            {
                AddToClassList("blocky-param-field");
                AddToClassList(UssClassName);
                // The owner's category class gives the hole its block's color (darkened below) through --blocky-fill.
                AddToClassList(BlockClasses.Category(ownerDefinition.category));
            }
            else
            {
                // A value slot is only a wrapper: the control inside it already carries blocky-param-field, and
                // having both would double the param's spacing.
                AddToClassList(ValueUssClassName);
            }

            var slotBlock = !prototype ? value?.reporter : null;
            if (slotBlock != null)
            {
                var definition = registry.Find(slotBlock.blockType);
                _block = definition != null
                    ? new ConditionView(slotBlock, definition, registry, stackId, store, ownerNodeId, spec.key)
                    : new UnknownBlockView(slotBlock);
                Add(_block);
            }
            else if (!IsConditionHole)
            {
                // An empty value input is still an editable field: typing a number is the common case, dropping a
                // reporter on it the rarer one, and a hole you cannot type into would be a step backwards.
                Add(BlockParams.CreateField(spec, value, ownerNodeId, stackId, store, prototype));
            }

            // Only an empty condition hole is drawn; a filled slot is the block's own outline, and an empty value
            // input is its control's own background.
            if (IsConditionHole)
                new BlockShapePainter(this, () => IsEmpty
                    ? new BlockOutline { Width = layout.width, Height = layout.height, Hexagon = true }
                    : default, EmptyShade);

            if (_store != null && IsConditionHole) RegisterCallback<ClickEvent>(OnClick);
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

    /// <summary>One param of a block's header. Every param is a slot now — a condition hole, or a value input that holds its own control until a reporter lands on it.</summary>
    internal static class BlockParams
    {
        public static VisualElement Create(ParamSpec spec, BlockParam value, BlockDefinition ownerDefinition, string nodeId,
            BlockRegistry registry, string stackId, ProgramStore store, bool prototype)
        {
            // A Choice is a fixed list, not a value a reporter could stand in for, so it stays a plain dropdown.
            if (spec.kind == ParamKind.Choice) return CreateField(spec, value, nodeId, stackId, store, prototype);
            return new ConditionSlot(spec, value, ownerDefinition, nodeId, registry, stackId, store, prototype);
        }

        /// <summary>The plain control for a param: a palette chip, a live field, or a read-only field.</summary>
        internal static VisualElement CreateField(ParamSpec spec, BlockParam value, string nodeId, string stackId, ProgramStore store, bool prototype)
        {
            if (prototype) return ParamFieldFactory.CreateChip(spec, value);
            if (store != null)
                return ParamFieldFactory.CreateLiveField(spec, value, newValue =>
                    store.Apply(new SetParam(new ParamTarget(stackId, nodeId), spec.key, newValue)));
            return ParamFieldFactory.CreateReadOnlyField(spec, value);
        }
    }
}
