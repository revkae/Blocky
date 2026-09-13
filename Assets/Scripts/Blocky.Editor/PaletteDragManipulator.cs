using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Drags a fresh block out of the palette and into the canvas (Scratch's "drag from library" gesture) —
    /// distinct from <see cref="BlockDragManipulator"/>, which reorders a block that already exists in a
    /// program. A trigger definition becomes a new <see cref="BlockStack"/> wherever it's dropped (nudged clear
    /// of existing stacks via <see cref="StackPlacementResolver"/>, same as "+ Add Stack"); a statement/C-block
    /// definition is inserted at the nearest <see cref="DropCandidate"/> across every stack on the canvas,
    /// reusing the exact snapping math <see cref="DropCandidateBuilder"/>/<see cref="DropCandidateResolver"/>
    /// already provide for in-canvas reordering. While dragging a statement/C-block, the current best candidate
    /// is shown as a glowing bar (<see cref="_snapIndicator"/>) at the exact rect it would snap into — the same
    /// "which side is it about to attach to" feedback Scratch gives, just drawn as a highlight strip rather than
    /// tinting the neighboring block itself (there's no need to reach into that block's own visuals).
    /// The ghost appears the instant the pointer goes down (no drag-threshold) — a palette item has no other
    /// click behaviour to protect against an accidental drag, unlike an existing block in the canvas.
    /// </summary>
    public sealed class PaletteDragManipulator : PointerManipulator
    {
        private static readonly Vector2 NominalStackSize = new(240f, 160f);

        private readonly BlockDefinition _definition;
        private readonly ProgramStore _store;
        private readonly ProgramCanvasView _canvasView;
        private readonly DragLayer _dragLayer;

        private VisualElement _ghost;
        private VisualElement _snapIndicator;
        private DropCandidate? _bestCandidate;
        private bool _dragging;

        public PaletteDragManipulator(VisualElement paletteItem, BlockDefinition definition, ProgramStore store,
            ProgramCanvasView canvasView, DragLayer dragLayer)
        {
            target = paletteItem;
            _definition = definition;
            _store = store;
            _canvasView = canvasView;
            _dragLayer = dragLayer;
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
            _dragging = true;
            _bestCandidate = null;
            target.CapturePointer(evt.pointerId);
            SpawnGhost();
            UpdateGhostPosition(evt.position);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging) return;
            UpdateGhostPosition(evt.position);
            UpdateSnapIndicator(evt.position);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            target.ReleasePointer(evt.pointerId);
            if (_dragging) CommitDrop(evt.position);
            CleanUp();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt) => CleanUp();

        private void SpawnGhost()
        {
            _ghost = new Label(string.IsNullOrEmpty(_definition.displayNameKey) ? _definition.blockType : _definition.displayNameKey);
            _ghost.AddToClassList("blocky-block");
            _ghost.AddToClassList($"blocky-block--category-{_definition.category.ToString().ToLowerInvariant()}");
            _ghost.AddToClassList("blocky-palette__ghost");
            _ghost.style.position = Position.Absolute;
            _ghost.pickingMode = PickingMode.Ignore;
            _dragLayer.Add(_ghost);
        }

        private void UpdateGhostPosition(Vector2 pointerPosition)
        {
            var local = _dragLayer.WorldToLocal(pointerPosition);
            _ghost.style.left = local.x;
            _ghost.style.top = local.y;
        }

        /// <summary>Recomputes the best drop candidate every move (cheap — a few dozen rects) and shows a glow bar there; hidden for trigger definitions, which don't snap to anything.</summary>
        private void UpdateSnapIndicator(Vector2 pointerPosition)
        {
            if (_definition.shape == BlockShape.Trigger)
            {
                _bestCandidate = null;
                HideIndicator();
                return;
            }

            var candidates = new List<DropCandidate>();
            foreach (var stackView in _canvasView.StackViews.Values)
                candidates.AddRange(DropCandidateBuilder.Build(stackView));

            _bestCandidate = DropCandidateResolver.FindBestCandidate(candidates, pointerPosition);

            if (_bestCandidate is not { } candidate)
            {
                HideIndicator();
                return;
            }

            _snapIndicator ??= CreateIndicator();
            _snapIndicator.style.display = DisplayStyle.Flex;
            var local = _dragLayer.WorldToLocal(new Vector2(candidate.Rect.x, candidate.Rect.y));
            _snapIndicator.style.left = local.x;
            _snapIndicator.style.top = local.y;
            _snapIndicator.style.width = candidate.Rect.width;
            _snapIndicator.style.height = Mathf.Max(candidate.Rect.height, 4f);
        }

        private VisualElement CreateIndicator()
        {
            var indicator = new VisualElement();
            indicator.AddToClassList("blocky-drop-indicator");
            indicator.style.position = Position.Absolute;
            indicator.pickingMode = PickingMode.Ignore;
            _dragLayer.Add(indicator);
            return indicator;
        }

        private void HideIndicator()
        {
            if (_snapIndicator != null) _snapIndicator.style.display = DisplayStyle.None;
        }

        private void CleanUp()
        {
            _ghost?.RemoveFromHierarchy();
            _ghost = null;
            _snapIndicator?.RemoveFromHierarchy();
            _snapIndicator = null;
            _bestCandidate = null;
            _dragging = false;
        }

        private void CommitDrop(Vector2 pointerPosition)
        {
            if (!_canvasView.worldBound.Contains(pointerPosition)) return; // released back over the palette/chrome — cancel

            if (_definition.shape == BlockShape.Trigger)
            {
                DropAsNewStack(pointerPosition);
                return;
            }

            if (_bestCandidate is not { } candidate) return; // no snap target under the pointer — nothing to attach to

            var location = candidate.ParentNodeId != null
                ? NodeLocation.InBranch(candidate.StackId, candidate.ParentNodeId, candidate.BranchIndex, candidate.Index)
                : NodeLocation.InStack(candidate.StackId, candidate.Index);

            _store.Apply(new InsertNode(location, PaletteView.InstantiatePrototype(_definition)));
        }

        private void DropAsNewStack(Vector2 pointerPosition)
        {
            var canvasLocal = _canvasView.WorldToLocal(pointerPosition);

            var existingPositions = new List<Vector2>();
            foreach (var s in _store.Program.stacks) existingPositions.Add(s.canvasPosition);

            var position = StackPlacementResolver.FindFreePosition(canvasLocal, existingPositions, NominalStackSize);
            var stack = new BlockStack { id = IdGenerator.NewId(), triggerBlockType = _definition.blockType, canvasPosition = position };
            _store.Apply(new CreateStack(stack));
        }
    }
}
