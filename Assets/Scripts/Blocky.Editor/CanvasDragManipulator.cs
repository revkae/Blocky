using System;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Picks up blocks already on the table, and selects them on a click (a second click on the only selected block
    /// unselects it; Shift- or Ctrl-click adds or removes a block, to pick several). Grabbing a block takes it and
    /// every block attached below it (Scratch's rule); grabbing a hat, or the first block of a loose stack, takes the
    /// whole stack; grabbing a condition takes it out of its slot. Grabbing one of several selected blocks moves
    /// them all (<see cref="GroupDragSession"/>). Drop it loose anywhere, snap it onto another block, or drop it on
    /// the palette to delete it.
    /// The press is heard in the TrickleDown phase — before any field inside the block gets it — so the whole
    /// block is a handle, fields included; only moving past the threshold turns a press into a drag. Pointer
    /// tracking itself lives in <see cref="TableDragManipulator"/>.
    /// </summary>
    public sealed class CanvasDragManipulator : TableDragManipulator
    {
        private const float DragThresholdPixels = 4f;

        private Vector2 _downPosition;
        private bool _pressedOnControl;
        private bool _additive; // Shift or Ctrl/Cmd held on press: add to the selection instead of replacing it
        private IDragSession _session;

        /// <param name="blockElement">A view implementing <see cref="IBlockElement"/> (block, hat or condition).</param>
        public CanvasDragManipulator(VisualElement blockElement, DragContext context) : base(blockElement, context)
        {
        }

        private IBlockElement Block => (IBlockElement)target;

        protected override void RegisterCallbacksOnTarget() =>
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

        protected override void UnregisterCallbacksFromTarget() =>
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

        private void OnPointerDown(PointerDownEvent evt)
        {
            var hit = evt.target as VisualElement;
            if (evt.button != 0 || IsTracking || Context.ActiveDrag != null || !IsOwnGrab(hit)) return;

            _downPosition = evt.position;
            _pressedOnControl = IsOnControl(hit);
            // Simple mode picks one block at a time: no multi-selection, so no group drag either.
            _additive = (evt.shiftKey || evt.actionKey) && Context.Mode == WorkspaceMode.Free;
            BeginTracking(evt.pointerId);
        }

        /// <summary>Starts the drag once the pointer has moved far enough, then moves the ghost. Returns true while dragging.</summary>
        protected override bool OnPointerMoved(Vector2 pointer)
        {
            if (_session == null)
            {
                if (Vector2.Distance(pointer, _downPosition) < DragThresholdPixels) return false; // still a click
                _session = StartSession(_downPosition);
                if (_session == null)
                {
                    StopTracking();
                    return false;
                }
            }

            _session.Move(pointer);
            return true;
        }

        /// <summary>A drag drops where the pointer is, a click selects or unselects. Returns true if it was a drag.</summary>
        protected override bool OnReleased(Vector2 pointer)
        {
            var session = _session;
            var pressedOnControl = _pressedOnControl;
            var additive = _additive;
            StopTracking(); // before Drop: the drop rebuilds the canvas, which replaces this manipulator's block

            if (session == null)
            {
                Click(pressedOnControl, additive);
                return false;
            }

            session.Drop(pointer);
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
            _pressedOnControl = false;
            _additive = false;
            _session = null;
        }

        /// <summary>
        /// Shift/Ctrl+click adds or removes this block. A plain click selects only this block, or unselects it when
        /// it's the only one selected. Clicking a checkbox or number box edits it — that never unselects anything.
        /// </summary>
        private void Click(bool pressedOnControl, bool additive)
        {
            var canvas = Context.Canvas;
            if (additive)
            {
                if (pressedOnControl) canvas.AddToSelection(Block.StackId, Block.NodeId);
                else canvas.ToggleSelected(Block.StackId, Block.NodeId);
            }
            else if (pressedOnControl)
            {
                if (!IsSelected()) SelectTarget();
            }
            else if (IsSelected() && canvas.Selection.Count == 1) canvas.ClearSelection();
            else SelectTarget();
        }

        private void SelectTarget() => Context.Canvas.Select(Block.StackId, Block.NodeId);

        private bool IsSelected() => Context.Canvas.IsSelected(Block.StackId, Block.NodeId);

        /// <summary>
        /// The press landed on this block itself and not on a block nested inside it (that one's own manipulator
        /// handles it). A dropdown is the one exception: it opens its menu on press, so it stays a plain control.
        /// </summary>
        private bool IsOwnGrab(VisualElement hit)
        {
            for (var el = hit; el != null; el = el.parent)
            {
                if (el.ClassListContains(BasePopupField<string, string>.ussClassName)) return false;
                if (el is IBlockElement) return el == target;
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
                if (el.ClassListContains(Toggle.inputUssClassName) || el.ClassListContains(BaseField<float>.inputUssClassName) ||
                    el.ClassListContains(ConditionSlot.UssClassName)) return true;
            return false;
        }

        private IDragSession StartSession(Vector2 pointer)
        {
            var stackView = target.GetFirstAncestorOfType<StackView>();
            if (stackView == null) return null;

            var canvas = Context.Canvas;
            canvas.panel?.focusController?.focusedElement?.Blur(); // a field pressed on the way in must not keep focus

            // Shift-dragging an unselected block adds it; dragging one of several selected blocks moves them all.
            if (_additive && !IsSelected()) canvas.AddToSelection(Block.StackId, Block.NodeId);
            if (IsSelected() && canvas.Selection.Count > 1) return GroupDragSession.Start(Context, canvas.Selection, pointer);

            SelectTarget(); // the block being moved is the selected one
            var zoom = Context.CanvasZoom();
            var program = Context.Store.Program;

            if (target is ConditionView { IsInSlot: true } condition)
            {
                // Out of its slot: the slot is left empty (and redraws as a hole) while the condition follows the pointer.
                var grab = pointer - condition.worldBound.position;
                var (stackId, ownerId, paramKey) = (condition.StackId, condition.OwnerNodeId, condition.ParamKey);
                condition.RemoveFromHierarchy();
                return new ChainDragSession(Context, condition, grab, zoom, ChainShape.Condition, fromCanvas: true,
                    t => DropChain.FromConditionSlot(stackId, ownerId, paramKey, t));
            }

            // A hat, or the first block of a loose stack (a lone condition lying on the table included): the whole stack moves.
            if (target is HatView || IsFirstBlockOfLooseStack(stackView))
            {
                var stack = ProgramQuery.FindStack(program, stackView.StackId);
                if (stack == null) return null;

                var grab = pointer - stackView.worldBound.position;
                var shape = ChainShape.Of(Context.Registry, stack.triggerBlockType, stack.sequence);
                var stackId = stack.id;
                stackView.RemoveFromHierarchy(); // also releases any pointer capture a control inside it held
                return new ChainDragSession(Context, stackView, grab, zoom, shape, fromCanvas: true, t => DropChain.FromStack(stackId, t));
            }

            var blockView = (BlockView)target;
            if (ProgramQuery.FindLocation(program, blockView.StackId, blockView.NodeId) is not { } location) return null;

            var container = location.ParentNodeId == null
                ? ProgramQuery.FindStack(program, location.StackId).sequence
                : ProgramQuery.FindNode(program, location.StackId, location.ParentNodeId).branches[location.BranchIndex];
            var chainShape = ChainShape.Of(Context.Registry, null, new ArraySegment<BlockNode>(container, location.Index, container.Length - location.Index));

            var grabOffset = pointer - blockView.worldBound.position;
            var siblings = SnapTargetCollector.BlockChildren(blockView.parent);
            var start = siblings.IndexOf(blockView);

            var ghost = new VisualElement();
            ghost.AddToClassList("blocky-drag-chain");
            foreach (var element in siblings.GetRange(start, siblings.Count - start)) ghost.Add(element); // Add re-parents out of the table

            return new ChainDragSession(Context, ghost, grabOffset, zoom, chainShape, fromCanvas: true, t => DropChain.FromNodes(location, t));
        }

        private bool IsFirstBlockOfLooseStack(StackView stackView) =>
            stackView.Hat == null && stackView.SequenceContainer.childCount > 0 && stackView.SequenceContainer.ElementAt(0) == target;
    }
}
