using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Wires real pointer events on a block's grab area to a <see cref="BlockDragController"/>: detach on cross-
    /// threshold, reparent into the <see cref="DragLayer"/> (last sibling — TDD §8.4's z-order rule), follow the
    /// pointer, and commit or restore on release. This adapter needs a live panel to do anything meaningful
    /// (world-space hit testing); the state machine and drop-candidate logic it drives are unit tested directly
    /// in <c>BlockDragControllerTests</c> and <c>DropCandidateBuilderTests</c> — this class is exercised once a
    /// real window hosts the canvas (Milestone 6+).
    /// </summary>
    public sealed class BlockDragManipulator : PointerManipulator
    {
        private readonly BlockDragController _controller;
        private readonly StackView _stackView;
        private readonly DragLayer _dragLayer;
        private readonly NodeLocation _origin;

        private VisualElement _draggedElement;
        private VisualElement _originalParent;
        private int _originalIndex;

        public BlockDragManipulator(VisualElement grabArea, BlockDragController controller, StackView stackView, DragLayer dragLayer, NodeLocation origin)
        {
            target = grabArea;
            _controller = controller;
            _stackView = stackView;
            _dragLayer = dragLayer;
            _origin = origin;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            _draggedElement = FindOwningBlockView();
            if (_draggedElement == null) return;

            _originalParent = _draggedElement.parent;
            _originalIndex = _originalParent.IndexOf(_draggedElement);

            _controller.BeginPickNode(_origin, evt.position);
            target.CapturePointer(evt.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_draggedElement == null) return;

            var wasDragging = _controller.State == DragState.Dragging;
            var candidates = DropCandidateBuilder.Build(_stackView);
            _controller.OnPointerMove(evt.position, candidates);

            if (!wasDragging && _controller.State == DragState.Dragging)
            {
                _draggedElement.RemoveFromHierarchy();
                _draggedElement.style.position = Position.Absolute;
                _dragLayer.Add(_draggedElement); // last sibling — always renders on top, survives a canvas clear
            }

            if (_controller.State == DragState.Dragging)
            {
                _draggedElement.style.left = evt.position.x;
                _draggedElement.style.top = evt.position.y;
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            target.ReleasePointer(evt.pointerId);
            _controller.CommitNodeDrop();
            RestoreIfStillDetached();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _controller.Cancel();
            RestoreIfStillDetached();
        }

        /// <summary>
        /// After a commit, the real fix-up is rebuilding the affected subtree from <c>ProgramStore.OnChanged</c>
        /// — that's Milestone 6's <c>ProgramCanvasView</c> job, once it exists. Until then, always put the view
        /// back where it started so a cancelled, rejected, or (for now) committed drag never leaves a visually
        /// orphaned block sitting in the drag layer.
        /// </summary>
        private void RestoreIfStillDetached()
        {
            if (_draggedElement == null || _draggedElement.parent != _dragLayer)
            {
                _draggedElement = null;
                return;
            }

            _draggedElement.RemoveFromHierarchy();
            _draggedElement.style.position = Position.Relative;
            _originalParent.Insert(_originalIndex, _draggedElement);
            _draggedElement = null;
        }

        private VisualElement FindOwningBlockView()
        {
            var el = target;
            while (el != null && el is not BlockView) el = el.parent;
            return el;
        }
    }
}
