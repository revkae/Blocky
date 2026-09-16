using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// A press-and-drag in progress. The host ends it through <see cref="ForceEnd"/> when the mouse button is found
    /// released but no pointer-up ever arrived — which happens when the button is let go somewhere the UI doesn't
    /// receive events for (over the game view, outside the window), and would otherwise leave the block glued to
    /// the cursor.
    /// </summary>
    public interface IActiveDrag
    {
        /// <summary>
        /// Called every frame while the button is held, with the real pointer position. Drives the drag even when
        /// UI move events don't reach the drag — e.g. a checkbox or text box inside the block grabbed the pointer.
        /// </summary>
        void Poll(Vector2 panelPointer);

        /// <param name="panelPointer">Where the pointer is now, in panel (world) coordinates.</param>
        void ForceEnd(Vector2 panelPointer);
    }

    /// <summary>One drag of blocks in progress — a single chain (<see cref="ChainDragSession"/>) or a selected group (<see cref="GroupDragSession"/>).</summary>
    public interface IDragSession
    {
        /// <param name="pointer">Panel (world) coordinates.</param>
        void Move(Vector2 pointer);

        /// <summary>Applies the outcome as one command.</summary>
        void Drop(Vector2 pointer);

        /// <summary>Abandons the drag; blocks lifted off the table go back.</summary>
        void Cancel();
    }

    /// <summary>
    /// Everything a drag on the table needs to know about its workspace. One per workspace for its whole life:
    /// <see cref="SetTarget"/> points it at the object being edited, so long-lived drag sources (the palette)
    /// never need rebuilding when the selection changes.
    /// </summary>
    public sealed class DragContext
    {
        /// <summary>The press/drag currently holding the pointer, if any. Only one at a time.</summary>
        public IActiveDrag ActiveDrag { get; internal set; }

        /// <summary>The edited object's program; null until an object is picked.</summary>
        public ProgramStore Store { get; private set; }

        public BlockRegistry Registry { get; }

        /// <summary>The edited object's table; null until an object is picked.</summary>
        public ProgramCanvasView Canvas { get; private set; }

        public DragLayer DragLayer { get; }
        public Func<Vector2, bool> IsOverCanvas { get; }
        public Func<Vector2, bool> IsOverDiscard { get; }

        /// <summary>The table's current zoom (1 = 100%), so a ghost over the table is drawn at the size it will land at.</summary>
        public Func<float> CanvasZoom { get; }

        /// <summary>An object is being edited — there's somewhere for a drag to land.</summary>
        public bool HasTarget => Store != null && Canvas != null;

        public DragContext(BlockRegistry registry, DragLayer dragLayer,
            Func<Vector2, bool> isOverCanvas, Func<Vector2, bool> isOverDiscard, Func<float> canvasZoom)
        {
            Registry = registry;
            DragLayer = dragLayer;
            IsOverCanvas = isOverCanvas;
            IsOverDiscard = isOverDiscard;
            CanvasZoom = canvasZoom;
        }

        /// <summary>The edited object changed: drags now read and write this program and this table.</summary>
        public void SetTarget(ProgramStore store, ProgramCanvasView canvas)
        {
            Store = store;
            Canvas = canvas;
        }
    }

    /// <summary>
    /// One drag in progress, whatever it started from (palette or table): moves the ghost, finds the nearest
    /// snap target and makes its edge glow, and on release turns the outcome into exactly one command — snapped
    /// if an edge was glowing, lying loose where it was dropped otherwise, deleted if dropped on the palette.
    /// The table is unbounded and can be zoomed, so every position here is worked out in two spaces: world
    /// (screen pixels, what the pointer and snap points use) and table-local (unscaled, what the program stores).
    /// </summary>
    public sealed class ChainDragSession : IDragSession
    {
        private const float SlotGlowPadding = 3f;

        private readonly DragContext _context;
        private readonly VisualElement _ghost;
        private readonly Vector2 _grabOffsetLocal; // where the pointer holds the chain, in unscaled block pixels
        private readonly ChainShape _shape;
        private readonly bool _fromCanvas;
        private readonly Func<ChainTarget, IProgramCommand> _makeCommand;

        // Snap targets are collected once and reused until the table changes under the drag: a pan or zoom (the
        // canvas transform) or a layout pass — the table re-flows the frame after blocks are lifted off it, and a
        // slot shrinks when its condition is pulled out. Lists are reused across refreshes.
        private readonly List<SnapTarget> _snapTargets = new();
        private readonly List<ConditionSlotTarget> _slotTargets = new();
        private readonly List<VisualElement> _watchedForLayout = new();
        private readonly EventCallback<GeometryChangedEvent> _onTableLayoutChanged;
        private bool _targetsDirty = true;
        private Matrix4x4 _targetsCanvasTransform;

        private VisualElement _indicator;
        private SnapTarget? _best;
        private ConditionSlotTarget? _bestSlot;
        private Vector2 _ghostTopLeft;
        private float _ghostScale = -1f;
        private Vector2 _lastPointer = new(float.NaN, float.NaN);

        /// <param name="grabOffset">Pointer position minus the chain's top-left, in world pixels at <paramref name="sourceScale"/>.</param>
        /// <param name="sourceScale">The zoom the chain was picked up at: the table's zoom, or 1 for the palette.</param>
        /// <param name="shape">What the chain can connect to — see <see cref="ChainShape"/>.</param>
        public ChainDragSession(DragContext context, VisualElement ghost, Vector2 grabOffset, float sourceScale, ChainShape shape,
            bool fromCanvas, Func<ChainTarget, IProgramCommand> makeCommand)
        {
            _context = context;
            _ghost = ghost;
            _grabOffsetLocal = grabOffset / Mathf.Max(sourceScale, 0.01f);
            _shape = shape;
            _fromCanvas = fromCanvas;
            _makeCommand = makeCommand;
            _onTableLayoutChanged = _ => _targetsDirty = true;

            WatchTableLayout();

            ghost.style.position = Position.Absolute;
            ghost.style.transformOrigin = new TransformOrigin(new Length(0f), new Length(0f), 0f);
            ghost.AddToClassList("blocky-drag-ghost");
            IgnorePicking(ghost); // the ghost sits under the pointer it follows — it must never become the hit target
            context.DragLayer.Add(ghost);
        }

        public void Move(Vector2 pointer)
        {
            var overCanvas = _context.IsOverCanvas(pointer);
            var scale = overCanvas ? _context.CanvasZoom() : 1f;

            // The host re-sends the held pointer every frame; with nothing moved and the table unchanged, there's nothing to redo.
            if (pointer == _lastPointer && Mathf.Approximately(scale, _ghostScale) && !TargetsStale) return;
            _lastPointer = pointer;

            if (!Mathf.Approximately(scale, _ghostScale))
            {
                _ghostScale = scale;
                _ghost.style.scale = new Scale(new Vector3(scale, scale, 1f));
            }

            _ghostTopLeft = pointer - _grabOffsetLocal * scale;
            var local = _context.DragLayer.WorldToLocal(_ghostTopLeft);
            _ghost.style.left = local.x;
            _ghost.style.top = local.y;

            _best = null;
            _bestSlot = null;
            if (overCanvas)
            {
                RefreshTargetsIfStale();
                if (_shape.IsCondition)
                    _bestSlot = ConditionSlotResolver.FindBest(_slotTargets, _ghostTopLeft + new Vector2(0f, LocalHeight * scale / 2f)); // the hexagon's left tip
                else
                    _best = SnapResolver.FindBest(_snapTargets,
                        new DraggedChain(_ghostTopLeft, _ghostTopLeft + new Vector2(0f, LocalHeight * scale), _shape.HasHat, _shape.EndsWithCap));
            }
            UpdateIndicator();
        }

        public void Drop(Vector2 pointer)
        {
            var target = ResolveTarget(pointer);
            CleanUp();

            if (target is not { } t)
            {
                if (_fromCanvas) _context.Canvas.Refresh(); // nothing changed — put the detached blocks back
                return;
            }

            try
            {
                _context.Store.Apply(_makeCommand(t));
            }
            catch (Exception e)
            {
                // The command restores the program on failure; rebuild so the table matches it again.
                Debug.LogException(e);
                _context.Canvas.Refresh();
            }
        }

        /// <summary>Abandons the drag: nothing is applied, and blocks picked up from the table go back where they were.</summary>
        public void Cancel()
        {
            CleanUp();
            if (_fromCanvas) _context.Canvas.Refresh();
        }

        private bool TargetsStale => _targetsDirty || _context.Canvas.worldTransform != _targetsCanvasTransform;

        private void RefreshTargetsIfStale()
        {
            if (!TargetsStale) return;
            _targetsDirty = false;
            _targetsCanvasTransform = _context.Canvas.worldTransform;

            if (_shape.IsCondition) ConditionSlotResolver.Collect(_context.Canvas, _slotTargets);
            else SnapTargetCollector.Collect(_context.Canvas, _snapTargets);
        }

        /// <summary>Any stack or condition slot left on the table re-flowing means cached snap targets moved.</summary>
        private void WatchTableLayout()
        {
            var canvas = _context.Canvas;
            foreach (var stackView in canvas.StackViews.Values)
                if (stackView.parent == canvas) Watch(stackView); // a stack being dragged isn't on the table
            canvas.Query<ConditionSlot>().ForEach(Watch);
        }

        private void Watch(VisualElement element)
        {
            element.RegisterCallback(_onTableLayoutChanged);
            _watchedForLayout.Add(element);
        }

        private ChainTarget? ResolveTarget(Vector2 pointer)
        {
            if (_context.IsOverDiscard(pointer)) return _fromCanvas ? ChainTarget.Discard() : null;
            if (!_context.IsOverCanvas(pointer)) return null;

            if (_bestSlot is { } slot)
            {
                // A condition already in the slot pops out just below-right of it: still in view, clear of the one dropped.
                var eject = _context.Canvas.WorldToLocal(new Vector2(slot.Bounds.xMax, slot.Bounds.yMax)) + new Vector2(24f, 24f);
                return ChainTarget.IntoConditionSlot(slot.StackId, slot.OwnerNodeId, slot.ParamKey, eject);
            }

            if (_best is { } snap)
            {
                if (snap.Kind != SnapKind.AboveStack) return ChainTarget.Insert(snap.InsertAt);

                // The merged stack's top moves up by the chain's height, so the old top block stays where it was.
                var stack = ProgramQuery.FindStack(_context.Store.Program, snap.StackId);
                return ChainTarget.AttachAbove(snap.StackId, stack.canvasPosition - new Vector2(0f, LocalHeight));
            }

            // The table is unbounded: any position is valid, negative ones included.
            return ChainTarget.Free(_context.Canvas.WorldToLocal(_ghostTopLeft));
        }

        /// <summary>The chain's height in table units — layout sizes are measured before the zoom transform.</summary>
        private float LocalHeight => float.IsNaN(_ghost.layout.height) ? 0f : _ghost.layout.height;

        private void UpdateIndicator()
        {
            Rect edge;
            if (_best is { } snap) edge = snap.Edge;
            else if (_bestSlot is { } slot)
                edge = new Rect(slot.Bounds.x - SlotGlowPadding, slot.Bounds.y - SlotGlowPadding,
                    slot.Bounds.width + SlotGlowPadding * 2f, slot.Bounds.height + SlotGlowPadding * 2f);
            else
            {
                if (_indicator != null) _indicator.style.display = DisplayStyle.None;
                return;
            }

            if (_indicator == null)
            {
                _indicator = new VisualElement();
                _indicator.AddToClassList("blocky-drop-indicator");
                _indicator.style.position = Position.Absolute;
                _indicator.pickingMode = PickingMode.Ignore;
                _context.DragLayer.Insert(_context.DragLayer.IndexOf(_ghost), _indicator); // under the ghost, not over it
            }

            _indicator.style.display = DisplayStyle.Flex;
            _indicator.EnableInClassList("blocky-drop-indicator--slot", _bestSlot != null); // a slot glows as a ring around the hole, not a bar
            var local = _context.DragLayer.WorldToLocal(edge.position);
            _indicator.style.left = local.x;
            _indicator.style.top = local.y;
            _indicator.style.width = edge.width;
            _indicator.style.height = edge.height;
        }

        private void CleanUp()
        {
            foreach (var element in _watchedForLayout) element.UnregisterCallback(_onTableLayoutChanged);
            _watchedForLayout.Clear();

            _ghost.RemoveFromHierarchy();
            _indicator?.RemoveFromHierarchy();
            _indicator = null;
        }

        internal static void IgnorePicking(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            foreach (var child in root.Children()) IgnorePicking(child);
        }
    }
}
