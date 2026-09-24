using System;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Takes a fresh block out of the palette. The ghost is a new prototype of the same definition, so it has the
    /// real silhouette while it moves. Released over the table it becomes one <see cref="DropChain"/> — snapped if
    /// an edge was glowing (a condition: dropped into a glowing slot), lying loose where it was dropped otherwise.
    /// Released anywhere else, nothing happens. Pointer tracking lives in <see cref="TableDragManipulator"/>.
    /// </summary>
    public sealed class PaletteDragManipulator : TableDragManipulator
    {
        private readonly BlockDefinition _definition;
        private readonly Func<BlockNode> _makeNode;
        private ChainDragSession _session;

        /// <param name="makeNode">
        /// Makes the block that gets dropped, for a palette entry that isn't the definition's defaults — a ready-made
        /// "run [jump]" for a custom block. Null: a fresh block with the definition's default values.
        /// </param>
        public PaletteDragManipulator(VisualElement paletteItem, BlockDefinition definition, DragContext context, Func<BlockNode> makeNode = null)
            : base(paletteItem, context)
        {
            _definition = definition;
            _makeNode = makeNode;
        }

        protected override void RegisterCallbacksOnTarget() => target.RegisterCallback<PointerDownEvent>(OnPointerDown);

        protected override void UnregisterCallbacksFromTarget() => target.UnregisterCallback<PointerDownEvent>(OnPointerDown);

        private void OnPointerDown(PointerDownEvent evt)
        {
            // Before any object is picked there's no table to drop on — the palette can be browsed, not dragged from.
            if (evt.button != 0 || IsTracking || Context.ActiveDrag != null || !Context.HasTarget) return;

            var ghost = BlockPrototype.Create(_definition, Context.Registry, _makeNode?.Invoke());
            _session = new ChainDragSession(Context, ghost, (Vector2)evt.position - target.worldBound.position, 1f,
                ChainShape.Of(_definition), fromCanvas: false, MakeCommand);
            _session.Move(evt.position);

            BeginTracking(evt.pointerId);
            evt.StopPropagation();
        }

        protected override bool OnPointerMoved(Vector2 pointer)
        {
            _session?.Move(pointer);
            return true;
        }

        protected override bool OnReleased(Vector2 pointer)
        {
            var session = _session;
            StopTracking();
            session?.Drop(pointer);
            return true;
        }

        protected override void OnCancelled()
        {
            var session = _session;
            StopTracking();
            session?.Cancel();
        }

        private void StopTracking()
        {
            EndTracking();
            _session = null;
        }

        private IProgramCommand MakeCommand(ChainTarget target)
        {
            var prototype = _makeNode?.Invoke() ?? PaletteView.InstantiatePrototype(_definition);
            return _definition.shape == BlockShape.Trigger
                ? DropChain.FromNewTrigger(_definition.blockType, prototype.parameters, target)
                : DropChain.FromNewNode(prototype, target);
        }
    }

    /// <summary>Palette rendering of a definition: the real silhouette with default values as static chips. The whole element is one drag handle — nothing inside it takes the pointer.</summary>
    public static class BlockPrototype
    {
        /// <param name="node">What the entry shows, when it isn't the definition's defaults (a ready-made "run [jump]").</param>
        public static VisualElement Create(BlockDefinition definition, BlockRegistry registry, BlockNode node = null)
        {
            node ??= PaletteView.InstantiatePrototype(definition);
            VisualElement view = definition.shape switch
            {
                BlockShape.Trigger => new HatView(definition, definition.blockType, node.parameters, null, null, prototype: true),
                BlockShape.Boolean or BlockShape.Reporter => ConditionView.CreatePrototype(definition, registry, node),
                _ => BlockView.CreatePrototype(definition, registry, node)
            };

            view.AddToClassList("blocky-palette__prototype");
            foreach (var child in view.Children()) ChainDragSession.IgnorePicking(child);
            return view;
        }
    }
}
