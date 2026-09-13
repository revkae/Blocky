using System.Collections.Generic;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Picks up blocks already on the table, and selects them on a click (a second click on the same block
    /// unselects it). Grabbing a block takes it and every block attached below it (Scratch's rule); grabbing a hat,
    /// or the first block of a loose stack, takes the whole stack. Drop it loose anywhere, snap it onto another
    /// block, or drop it on the palette to delete it.
    /// The press is heard in the TrickleDown phase — before any field inside the block gets it — so the whole
    /// block is a handle, fields included; only moving past the threshold turns a press into a drag. Movement is
    /// tracked both from UI events on the panel root and from <see cref="Poll"/> (the host reads the real mouse
    /// every frame), because a control inside the block — a checkbox, a text box — captures the pointer on press
    /// and would otherwise keep the move events to itself.
    /// </summary>
    public sealed class CanvasDragManipulator : PointerManipulator, IActiveDrag
    {
        private const float DragThresholdPixels = 4f;

        private readonly DragContext _context;
        private VisualElement _tree;
        private int _pointerId = -1;
        private Vector2 _downPosition;
        private bool _pressedOnControl;
        private bool _eventMovedSincePoll;
        private ChainDragSession _session;

        public CanvasDragManipulator(VisualElement blockOrHat, DragContext context)
        {
            target = blockOrHat;
            _context = context;
        }

        protected override void RegisterCallbacksOnTarget() =>
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

        protected override void UnregisterCallbacksFromTarget() =>
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

        public void Poll(Vector2 panelPointer)
        {
            if (_pointerId == -1) return;

            // UI move events are the primary source; the poll only fills in when they've stopped arriving (a control
            // inside the block captured the pointer). Driving from both sources every frame made the ghost jitter.
            if (_eventMovedSincePoll)
            {
                _eventMovedSincePoll = false;
                return;
            }
            Track(panelPointer);
        }

        public void ForceEnd(Vector2 panelPointer)
        {
            if (_pointerId != -1) Finish(panelPointer);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            var hit = evt.target as VisualElement;
            if (evt.button != 0 || _pointerId != -1 || _context.ActiveDrag != null || !IsOwnGrab(hit)) return;

            _pointerId = evt.pointerId;
            _downPosition = evt.position;
            _pressedOnControl = IsOnControl(hit);
            _tree = target.panel.visualTree;
            _tree.RegisterCallback<PointerMoveEvent>(OnTreeMove, TrickleDown.TrickleDown);
            _tree.RegisterCallback<PointerUpEvent>(OnTreeUp, TrickleDown.TrickleDown);
            _tree.RegisterCallback<PointerCancelEvent>(OnTreeCancel, TrickleDown.TrickleDown);
            _context.ActiveDrag = this;
        }

        private void OnTreeMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointerId) return;
            _eventMovedSincePoll = true;
            if (Track(evt.position)) evt.StopPropagation();
        }

        private void OnTreeUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointerId) return;
            if (Finish(evt.position)) evt.StopPropagation();
        }

        private void OnTreeCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId != _pointerId) return;
            var session = _session;
            EndTracking();
            session?.Cancel();
        }

        /// <summary>Follows the pointer: starts the drag once it has moved far enough, then moves the ghost. Returns true while dragging.</summary>
        private bool Track(Vector2 pointer)
        {
            if (_session == null)
            {
                if (Vector2.Distance(pointer, _downPosition) < DragThresholdPixels) return false; // still a click
                _session = StartSession(_downPosition);
                if (_session == null)
                {
                    EndTracking();
                    return false;
                }
            }

            _session.Move(pointer);
            return true;
        }

        /// <summary>Ends the gesture: a drag drops where the pointer is, a click selects or unselects. Returns true if it was a drag.</summary>
        private bool Finish(Vector2 pointer)
        {
            var session = _session;
            var pressedOnControl = _pressedOnControl;
            EndTracking(); // before Drop: the drop rebuilds the canvas, which replaces this manipulator's block

            if (session == null)
            {
                // Clicking a checkbox or a number box edits it — that must never unselect the block around it.
                if (pressedOnControl || !IsSelected()) SelectTarget();
                else _context.Canvas.ClearSelection();
                return false;
            }

            session.Drop(pointer);
            return true;
        }

        private void EndTracking()
        {
            _tree?.UnregisterCallback<PointerMoveEvent>(OnTreeMove, TrickleDown.TrickleDown);
            _tree?.UnregisterCallback<PointerUpEvent>(OnTreeUp, TrickleDown.TrickleDown);
            _tree?.UnregisterCallback<PointerCancelEvent>(OnTreeCancel, TrickleDown.TrickleDown);
            _tree = null;
            _pointerId = -1;
            _pressedOnControl = false;
            _eventMovedSincePoll = false;
            _session = null;
            if (_context.ActiveDrag == this) _context.ActiveDrag = null;
        }

        private void SelectTarget()
        {
            if (target is HatView hat) _context.Canvas.Select(hat.StackId, null);
            else if (target is BlockView block) _context.Canvas.Select(block.StackId, block.NodeId);
            else if (target is ConditionView condition) _context.Canvas.Select(condition.StackId, condition.NodeId);
        }

        private bool IsSelected()
        {
            var canvas = _context.Canvas;
            if (!canvas.HasSelection) return false;
            if (target is HatView hat) return canvas.SelectedNodeId == null && canvas.SelectedStackId == hat.StackId;
            if (target is ConditionView condition) return canvas.SelectedNodeId == condition.NodeId;
            return target is BlockView block && canvas.SelectedNodeId == block.NodeId;
        }

        /// <summary>
        /// The press landed on this block itself and not on a block nested inside it (that one's own manipulator
        /// handles it). A dropdown is the one exception: it opens its menu on press, so it stays a plain control.
        /// </summary>
        private bool IsOwnGrab(VisualElement hit)
        {
            for (var el = hit; el != null; el = el.parent)
            {
                if (el.ClassListContains("unity-base-popup-field")) return false;
                if (el is BlockView || el is HatView || el is ConditionView) return el == target;
            }
            return false;
        }

        /// <summary>
        /// True when the press is on an input's own box — the checkbox square, a number or text box, an empty
        /// condition slot (which opens its dropdown) — rather than on the block.
        /// </summary>
        private bool IsOnControl(VisualElement hit)
        {
            for (var el = hit; el != null && el != target; el = el.parent)
                if (el.ClassListContains("unity-toggle__input") || el.ClassListContains("unity-base-field__input") ||
                    el.ClassListContains(ConditionSlot.UssClassName)) return true;
            return false;
        }

        private ChainDragSession StartSession(Vector2 pointer)
        {
            var stackView = FindStackView(target);
            if (stackView == null) return null;

            SelectTarget(); // the block being moved is the selected one
            _context.Canvas.panel?.focusController?.focusedElement?.Blur(); // a field pressed on the way in must not keep focus
            var zoom = _context.CanvasZoom();

            if (target is ConditionView condition)
            {
                if (condition.IsInSlot)
                {
                    // Out of its slot: the slot is left empty (and redraws as a hole) while the condition follows the pointer.
                    var grab = pointer - condition.worldBound.position;
                    var (stackId, ownerId, paramKey) = (condition.StackId, condition.OwnerNodeId, condition.ParamKey);
                    condition.RemoveFromHierarchy();
                    return new ChainDragSession(_context, condition, grab, zoom, hasHat: false, endsWithCap: false, fromCanvas: true,
                        t => DropChain.FromConditionSlot(stackId, ownerId, paramKey, t), isCondition: true);
                }

                // Lying loose: it's the only block of its loose stack, so the whole stack moves.
                var looseGrab = pointer - stackView.worldBound.position;
                var looseStackId = stackView.StackId;
                stackView.RemoveFromHierarchy();
                return new ChainDragSession(_context, stackView, looseGrab, zoom, hasHat: false, endsWithCap: false, fromCanvas: true,
                    t => DropChain.FromStack(looseStackId, t), isCondition: true);
            }

            if (target is HatView || IsFirstBlockOfLooseStack(stackView))
            {
                var grab = pointer - stackView.worldBound.position;
                var endsWithCap = EndsWithCap(stackView.SequenceContainer);
                stackView.RemoveFromHierarchy(); // also releases any pointer capture a control inside it held
                var stackId = stackView.StackId;
                return new ChainDragSession(_context, stackView, grab, zoom, target is HatView, endsWithCap, fromCanvas: true,
                    t => DropChain.FromStack(stackId, t));
            }

            var blockView = (BlockView)target;
            var from = ProgramQuery.FindLocation(_context.Store.Program, blockView.StackId, blockView.NodeId);
            if (from == null) return null;

            var grabOffset = pointer - blockView.worldBound.position;
            var moving = new List<VisualElement>();
            var started = false;
            foreach (var child in blockView.parent.Children())
            {
                if (child == blockView) started = true;
                if (started && (child is BlockView || child is UnknownBlockView)) moving.Add(child);
            }

            var ghost = new VisualElement();
            ghost.AddToClassList("blocky-drag-chain");
            foreach (var element in moving) ghost.Add(element); // Add re-parents out of the table

            var location = from.Value;
            return new ChainDragSession(_context, ghost, grabOffset, zoom, hasHat: false, EndsWithCap(ghost), fromCanvas: true,
                t => DropChain.FromNodes(location, t));
        }

        private bool IsFirstBlockOfLooseStack(StackView stackView)
        {
            if (stackView.Hat != null || target.parent != stackView.SequenceContainer) return false;
            foreach (var child in stackView.SequenceContainer.Children())
                if (child is BlockView || child is UnknownBlockView) return child == target;
            return false;
        }

        private static bool EndsWithCap(VisualElement container)
        {
            VisualElement last = null;
            foreach (var child in container.Children())
                if (child is BlockView || child is UnknownBlockView) last = child;
            return last is BlockView blockView && blockView.IsTerminal;
        }

        private static StackView FindStackView(VisualElement element)
        {
            for (var el = element; el != null; el = el.parent)
                if (el is StackView stackView) return stackView;
            return null;
        }
    }
}
