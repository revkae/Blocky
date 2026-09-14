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
        private ChainDragSession _session;

        public PaletteDragManipulator(VisualElement paletteItem, BlockDefinition definition, DragContext context) : base(paletteItem, context)
        {
            _definition = definition;
        }

        protected override void RegisterCallbacksOnTarget() => target.RegisterCallback<PointerDownEvent>(OnPointerDown);

        protected override void UnregisterCallbacksFromTarget() => target.UnregisterCallback<PointerDownEvent>(OnPointerDown);

        private void OnPointerDown(PointerDownEvent evt)
        {
            // Before any object is picked there's no table to drop on — the palette can be browsed, not dragged from.
            if (evt.button != 0 || IsTracking || Context.ActiveDrag != null || !Context.HasTarget) return;

            var ghost = BlockPrototype.Create(_definition, Context.Registry);
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
            var prototype = PaletteView.InstantiatePrototype(_definition);
            return _definition.shape == BlockShape.Trigger
                ? DropChain.FromNewTrigger(_definition.blockType, prototype.parameters, target)
                : DropChain.FromNewNode(prototype, target);
        }
    }

    /// <summary>Palette rendering of a definition: the real silhouette with default values as static chips. The whole element is one drag handle — nothing inside it takes the pointer.</summary>
    public static class BlockPrototype
    {
        public static VisualElement Create(BlockDefinition definition, BlockRegistry registry)
        {
            VisualElement view = definition.shape switch
            {
                BlockShape.Trigger => new HatView(definition, definition.blockType, PaletteView.InstantiatePrototype(definition).parameters, null, null, prototype: true),
                BlockShape.Boolean => ConditionView.CreatePrototype(definition, registry),
                _ => BlockView.CreatePrototype(definition, registry)
            };

            view.AddToClassList("blocky-palette__prototype");
            foreach (var child in view.Children()) ChainDragSession.IgnorePicking(child);
            return view;
        }
    }
}
