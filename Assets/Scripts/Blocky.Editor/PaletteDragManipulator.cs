using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Takes a fresh block out of the palette. The ghost is a new prototype of the same definition, so it has the
    /// real silhouette while it moves. Released over the table it becomes one <see cref="DropChain"/> — snapped if
    /// an edge was glowing, lying loose where it was dropped otherwise. Released anywhere else, nothing happens.
    /// Tracks the pointer on the panel root (see <see cref="CanvasDragManipulator"/> for why not capture), and can
    /// be ended by the host through <see cref="IActiveDrag"/> if the pointer-up never arrives.
    /// </summary>
    public sealed class PaletteDragManipulator : PointerManipulator, IActiveDrag
    {
        private readonly BlockDefinition _definition;
        private readonly DragContext _context;
        private VisualElement _tree;
        private int _pointerId = -1;
        private ChainDragSession _session;
        private bool _eventMovedSincePoll;

        public PaletteDragManipulator(VisualElement paletteItem, BlockDefinition definition, DragContext context)
        {
            target = paletteItem;
            _definition = definition;
            _context = context;
        }

        protected override void RegisterCallbacksOnTarget() => target.RegisterCallback<PointerDownEvent>(OnPointerDown);

        protected override void UnregisterCallbacksFromTarget() => target.UnregisterCallback<PointerDownEvent>(OnPointerDown);

        public void Poll(Vector2 panelPointer)
        {
            if (_pointerId == -1) return;

            // UI move events are the primary source; the poll only fills in when they stop arriving.
            if (_eventMovedSincePoll)
            {
                _eventMovedSincePoll = false;
                return;
            }
            _session?.Move(panelPointer);
        }

        public void ForceEnd(Vector2 panelPointer)
        {
            if (_pointerId != -1) Finish(panelPointer);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _pointerId != -1 || _context.ActiveDrag != null) return;

            _pointerId = evt.pointerId;
            var ghost = BlockPrototype.Create(_definition, _context.Registry);
            _session = new ChainDragSession(_context, ghost, (Vector2)evt.position - target.worldBound.position, 1f,
                hasHat: _definition.shape == BlockShape.Trigger, endsWithCap: _definition.shape == BlockShape.Cap,
                fromCanvas: false, MakeCommand, isCondition: _definition.shape == BlockShape.Boolean);
            _session.Move(evt.position);

            _tree = target.panel.visualTree;
            _tree.RegisterCallback<PointerMoveEvent>(OnTreeMove, TrickleDown.TrickleDown);
            _tree.RegisterCallback<PointerUpEvent>(OnTreeUp, TrickleDown.TrickleDown);
            _tree.RegisterCallback<PointerCancelEvent>(OnTreeCancel, TrickleDown.TrickleDown);
            _context.ActiveDrag = this;
            evt.StopPropagation();
        }

        private void OnTreeMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointerId) return;
            _eventMovedSincePoll = true;
            _session.Move(evt.position);
            evt.StopPropagation();
        }

        private void OnTreeUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointerId) return;
            Finish(evt.position);
            evt.StopPropagation();
        }

        private void OnTreeCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId != _pointerId) return;
            var session = _session;
            EndTracking();
            session?.Cancel();
        }

        private void Finish(Vector2 pointer)
        {
            var session = _session;
            EndTracking();
            session?.Drop(pointer);
        }

        private void EndTracking()
        {
            _tree?.UnregisterCallback<PointerMoveEvent>(OnTreeMove, TrickleDown.TrickleDown);
            _tree?.UnregisterCallback<PointerUpEvent>(OnTreeUp, TrickleDown.TrickleDown);
            _tree?.UnregisterCallback<PointerCancelEvent>(OnTreeCancel, TrickleDown.TrickleDown);
            _tree = null;
            _pointerId = -1;
            _session = null;
            _eventMovedSincePoll = false;
            if (_context.ActiveDrag == this) _context.ActiveDrag = null;
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
