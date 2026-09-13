using System;
using System.Collections.Generic;
using Blocky.Data;
using UnityEngine;

namespace Blocky.Editor
{
    /// <summary>
    /// The drag state machine (TDD §8.3), deliberately decoupled from UI Toolkit pointer events so it can be
    /// driven directly in tests. <see cref="BlockDragManipulator"/> is the thin adapter that wires real pointer
    /// events to this. The model is untouched until a commit method is called, and exactly one command is ever
    /// issued per drag — preview during <see cref="DragState.Dragging"/> is visual only (TDD §8.3).
    /// </summary>
    public sealed class BlockDragController
    {
        /// <summary>Editor soft-cap on nesting depth (TDD §8.4). A deeper drop is rejected, not silently truncated.</summary>
        public const int MaxNestingDepth = 12;

        private const float DragThresholdPixels = 4f;

        private readonly ProgramStore _store;

        private enum PickKind { None, Node, Stack }
        private PickKind _pickKind = PickKind.None;

        private NodeLocation _nodeOrigin;
        private string _draggedStackId;
        private Vector2 _pointerDownPosition;
        private Vector2 _lastPointerPosition;
        private DropCandidate? _bestCandidate;

        public DragState State { get; private set; } = DragState.Idle;

        /// <summary>Raised whenever the resolved drop candidate changes, so a caller can update a preview highlight.</summary>
        public event Action<DropCandidate?> OnCandidateChanged;

        /// <summary>Raised when a drop was rejected (nesting cap) instead of committed.</summary>
        public event Action<string> OnDropRejected;

        public BlockDragController(ProgramStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public void BeginPickNode(NodeLocation origin, Vector2 pointerPosition)
        {
            RequireIdle();
            _pickKind = PickKind.Node;
            _nodeOrigin = origin;
            _pointerDownPosition = pointerPosition;
            State = DragState.Picked;
        }

        public void BeginPickStack(string stackId, Vector2 pointerPosition)
        {
            RequireIdle();
            _pickKind = PickKind.Stack;
            _draggedStackId = stackId;
            _pointerDownPosition = pointerPosition;
            State = DragState.Picked;
        }

        public void OnPointerMove(Vector2 pointerPosition, IReadOnlyList<DropCandidate> candidates)
        {
            _lastPointerPosition = pointerPosition;

            if (State == DragState.Picked)
            {
                if (Vector2.Distance(pointerPosition, _pointerDownPosition) < DragThresholdPixels) return;
                State = DragState.Dragging;
            }

            if (State != DragState.Dragging) return;

            // Whole-stack drags target open canvas space (resolved at commit via StackPlacementResolver),
            // not a node drop candidate.
            var candidate = _pickKind == PickKind.Node ? DropCandidateResolver.FindBestCandidate(candidates, pointerPosition) : null;
            _bestCandidate = candidate;
            OnCandidateChanged?.Invoke(candidate);
        }

        public void CommitNodeDrop()
        {
            if (State == DragState.Picked) { Reset(); return; } // never crossed the threshold — a click, not a move
            if (State != DragState.Dragging || _pickKind != PickKind.Node) { Reset(); return; }

            State = DragState.Committing;

            if (_bestCandidate is { } candidate)
            {
                if (candidate.Depth >= MaxNestingDepth)
                {
                    OnDropRejected?.Invoke($"Nesting depth cap ({MaxNestingDepth}) reached.");
                }
                else
                {
                    var to = AdjustForSameContainerShift(_nodeOrigin, ToLocation(candidate));
                    _store.Apply(new MoveNode(_nodeOrigin, to));
                }
            }
            // No candidate under the pointer: drop back at origin. The model was never touched, so there's
            // nothing to undo — the view is simply put back where it started (BlockDragManipulator's job).

            Reset();
        }

        public void CommitStackDrop(IReadOnlyList<Vector2> otherStackPositions, Vector2 stackSize)
        {
            if (State == DragState.Picked) { Reset(); return; }
            if (State != DragState.Dragging || _pickKind != PickKind.Stack) { Reset(); return; }

            State = DragState.Committing;

            var freePosition = StackPlacementResolver.FindFreePosition(_lastPointerPosition, otherStackPositions, stackSize);
            _store.Apply(new MoveStack(_draggedStackId, freePosition));

            Reset();
        }

        /// <summary>Esc, lost pointer capture, or an external canvas reset — forces Idle unconditionally (TDD §8.4).</summary>
        public void Cancel() => Reset();

        private static NodeLocation ToLocation(DropCandidate candidate) => candidate.ParentNodeId != null
            ? NodeLocation.InBranch(candidate.StackId, candidate.ParentNodeId, candidate.BranchIndex, candidate.Index)
            : NodeLocation.InStack(candidate.StackId, candidate.Index);

        /// <summary>
        /// A gap/end candidate's index is expressed against the *pre-removal* sequence. If the drop lands in the
        /// same container the node is already in, and after where it currently sits, removing it first shifts
        /// every later index down by one — so the target index must be adjusted before <see cref="MoveNode"/>
        /// runs (its own doc comment calls this out as the caller's responsibility, not something it guesses).
        /// </summary>
        private static NodeLocation AdjustForSameContainerShift(NodeLocation from, NodeLocation to)
        {
            var sameContainer = from.StackId == to.StackId && from.ParentNodeId == to.ParentNodeId && from.BranchIndex == to.BranchIndex;
            return sameContainer && from.Index < to.Index
                ? new NodeLocation(to.StackId, to.ParentNodeId, to.BranchIndex, to.Index - 1)
                : to;
        }

        private void RequireIdle()
        {
            if (State != DragState.Idle)
                throw new InvalidOperationException($"Cannot begin a new drag while in state {State}.");
        }

        private void Reset()
        {
            _pickKind = PickKind.None;
            _draggedStackId = null;
            _bestCandidate = null;
            State = DragState.Idle;
        }
    }
}
