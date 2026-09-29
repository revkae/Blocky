using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// The press → drag → release lifecycle shared by every drag on the in-game table. The pointer is followed on
    /// the panel root (TrickleDown callbacks) and also through <see cref="IActiveDrag"/>: the host feeds the real
    /// mouse every frame, because a control inside a block (checkbox, text box) can capture the pointer on press
    /// and keep the move events to itself, and a release over the game view never reaches the UI at all.
    /// Deliberately no pointer capture of our own: a captured pointer's events go to the capturing element, not
    /// through these root callbacks, which froze the ghost until release (see the Build Log, 2026-09-14).
    /// UI move events stay the primary source; <see cref="Poll"/> only moves the drag when none arrived since the
    /// previous poll — driving from both every frame made the ghost jitter. Subclasses decide what a press starts
    /// and what moving/releasing does, and call <see cref="EndTracking"/> when the gesture is over.
    /// </summary>
    public abstract class TableDragManipulator : PointerManipulator, IActiveDrag
    {
        protected readonly DragContext Context;

        private VisualElement _tree;
        private int _pointerId = -1;
        private bool _eventMovedSincePoll;

        protected TableDragManipulator(VisualElement target, DragContext context)
        {
            this.target = target;
            Context = context;
        }

        protected bool IsTracking => _pointerId != -1;

        public void Poll(Vector2 panelPointer)
        {
            if (!IsTracking) return;
            if (_eventMovedSincePoll)
            {
                _eventMovedSincePoll = false;
                return;
            }
            OnPointerMoved(panelPointer);
        }

        public void ForceEnd(Vector2 panelPointer)
        {
            if (IsTracking) OnReleased(panelPointer);
        }

        /// <summary>Starts following <paramref name="pointerId"/> on the panel root and claims the context's single active drag.</summary>
        protected void BeginTracking(int pointerId)
        {
            _pointerId = pointerId;
            _tree = target.panel.visualTree;
            _tree.RegisterCallback<PointerMoveEvent>(OnTreeMove, TrickleDown.TrickleDown);
            _tree.RegisterCallback<PointerUpEvent>(OnTreeUp, TrickleDown.TrickleDown);
            _tree.RegisterCallback<PointerCancelEvent>(OnTreeCancel, TrickleDown.TrickleDown);
            Context.ActiveDrag = this;
        }

        protected void EndTracking()
        {
            _tree?.UnregisterCallback<PointerMoveEvent>(OnTreeMove, TrickleDown.TrickleDown);
            _tree?.UnregisterCallback<PointerUpEvent>(OnTreeUp, TrickleDown.TrickleDown);
            _tree?.UnregisterCallback<PointerCancelEvent>(OnTreeCancel, TrickleDown.TrickleDown);
            _tree = null;
            _pointerId = -1;
            _eventMovedSincePoll = false;
            if (Context.ActiveDrag == this) Context.ActiveDrag = null;
        }

        /// <summary>The pointer moved while tracking. Returns true to stop the move event from propagating.</summary>
        protected abstract bool OnPointerMoved(Vector2 pointer);

        /// <summary>The button came up (or was found released). Must end tracking. Returns true to stop the up event from propagating.</summary>
        protected abstract bool OnReleased(Vector2 pointer);

        /// <summary>The panel cancelled the pointer. Must end tracking.</summary>
        protected abstract void OnCancelled();

        private void OnTreeMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointerId) return;
            _eventMovedSincePoll = true;
            if (OnPointerMoved(evt.position)) evt.StopPropagation();
        }

        private void OnTreeUp(PointerUpEvent evt)
        {
            if (evt.pointerId == _pointerId && OnReleased(evt.position)) evt.StopPropagation();
        }

        private void OnTreeCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId == _pointerId) OnCancelled();
        }
    }
}
