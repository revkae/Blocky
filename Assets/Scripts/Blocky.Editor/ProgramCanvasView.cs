using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>How a <see cref="ProgramCanvasView"/> can be edited.</summary>
    public enum CanvasMode
    {
        /// <summary>A preview: every field disabled, nothing editable.</summary>
        ReadOnly,

        /// <summary>The Editor window: live fields plus per-block ✕ and "+ Add" buttons.</summary>
        Buttons,

        /// <summary>The in-game table: live fields and no buttons — blocks are placed, moved and deleted purely by drag and drop (the host attaches the drag manipulators on <see cref="ProgramCanvasView.Rebuilt"/>).</summary>
        Table
    }

    /// <summary>
    /// Hosts every stack, positioned by <c>canvasPosition</c>, and rebuilds whenever
    /// <see cref="ProgramStore.OnChanged"/> fires (TDD §8.2). Rebuilds the whole canvas on any change rather
    /// than patching the affected subtree — correct and simple; subtree patching is an optimization.
    /// See <see cref="CanvasMode"/> for the three ways it can be used.
    /// Also owns which block is selected and which blocks are running, so both highlights survive the rebuild
    /// every edit causes, and pins advice badges on blocks.
    /// </summary>
    public sealed class ProgramCanvasView : VisualElement
    {
        // A stack's real footprint depends on its content, which we don't know without a live panel — this is a
        // deliberately generous nominal size so virtualization never hides a stack that would be partly visible.
        private const float NominalStackWidth = 240f;
        private const float NominalStackHeight = 160f;
        private const float ViewportMargin = 200f;

        public const string AdviceBadgeClass = "blocky-advice-badge";

        /// <summary>On the canvas while it is a single strict column (<see cref="WorkspaceMode.Simple"/>).</summary>
        public const string SimpleClass = "blocky-canvas--simple";

        public const string RowLayerClass = "blocky-canvas__rows";
        public const string RowClass = CanvasRow.UssClassName;
        public const string RowNumberClass = CanvasRow.NumberUssClassName;

        private readonly ProgramStore _store;
        private readonly BlockRegistry _registry;
        private readonly bool _live;
        private readonly bool _buttons;
        private readonly Dictionary<string, StackView> _stackViews = new();
        private readonly Dictionary<string, VisualElement> _blocksByNodeId = new(); // rebuilt with the views; hats are found through their stack
        private Rect? _viewport;

        private readonly List<BlockRef> _selection = new(); // in the order picked; the last one is the "primary"
        private readonly List<VisualElement> _selectedElements = new();

        private readonly HashSet<string> _runningNodeIds = new();
        private readonly List<VisualElement> _runningElements = new();
        private readonly List<VisualElement> _adviceBadges = new();

        private readonly EventCallback<GeometryChangedEvent> _onStackLaidOut;

        private WorkspaceMode _mode = WorkspaceMode.Free;
        private VisualElement _rowLayer;                              // Simple mode: the numbered bands, behind the blocks
        private readonly List<CanvasRow> _rowPool = new();             // reused across layout passes
        private readonly List<RowBand> _rows = new();
        private readonly List<CanvasRow> _shownRows = new();            // the bands in use right now, in reading order

        // Reading order: down the column, and a block that contains another comes first — so a nested band is added
        // after the band around it and therefore sits on top of it, both to click and to paint.
        // Cached: sorting on every layout pass must not allocate.
        private readonly System.Comparison<RowBand> _inReadingOrder = (a, b) =>
            Mathf.Approximately(a.Y, b.Y) ? b.Height.CompareTo(a.Height) : a.Y.CompareTo(b.Y);

        /// <summary>A block's strip on the column while rows are being measured.</summary>
        private readonly struct RowBand
        {
            public readonly float Y;
            public readonly float Height;
            public readonly BlockRef Block;

            public RowBand(float y, float height, BlockRef block)
            {
                Y = y;
                Height = height;
                Block = block;
            }
        }

        public IReadOnlyDictionary<string, StackView> StackViews => _stackViews;

        /// <summary>
        /// Free table or one strict column — see <see cref="WorkspaceMode"/>. Changing it re-lays out every stack;
        /// the program is not touched, so the same object can be opened either way.
        /// </summary>
        public WorkspaceMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                Rebuild();
            }
        }

        /// <summary>Raised after every rebuild, so a host can decorate the fresh block views (e.g. attach drag manipulators).</summary>
        public event Action Rebuilt;

        public bool HasSelection => _selection.Count > 0;

        /// <summary>Every selected block (Shift/Ctrl+click or a selection box picks several), in the order they were picked.</summary>
        public IReadOnlyList<BlockRef> Selection => _selection;

        /// <summary>The stack of the most recently selected block; null when nothing is selected.</summary>
        public string SelectedStackId => HasSelection ? _selection[^1].StackId : null;

        /// <summary>The most recently selected block; null when nothing is selected or it's a hat.</summary>
        public string SelectedNodeId => HasSelection ? _selection[^1].NodeId : null;

        public ProgramCanvasView(ProgramStore store, BlockRegistry registry, CanvasMode mode = CanvasMode.ReadOnly)
        {
            _store = store;
            _registry = registry;
            _live = mode != CanvasMode.ReadOnly;
            _buttons = mode == CanvasMode.Buttons;
            AddToClassList("blocky-canvas");
            style.position = Position.Relative;
            _onStackLaidOut = _ => UpdateRows();
            RegisterCallback(_onStackLaidOut); // a block's place in the column is only known once measured

            Rebuild();
            _store.OnChanged += _ => Rebuild();
        }

        /// <summary>
        /// Sets the visible viewport in canvas space; stacks entirely outside it (plus a margin) are not
        /// instantiated at all (TDD §8.4). Pass <c>null</c> to disable virtualization.
        /// </summary>
        public void SetViewport(Rect? viewport)
        {
            _viewport = viewport;
            Rebuild();
        }

        /// <summary>Rebuilds from the current program — e.g. to put back blocks a cancelled drag had detached.</summary>
        public void Refresh() => Rebuild();

        /// <summary>Selects only this block (<paramref name="nodeId"/>) or, with a null node id, the hat of <paramref name="stackId"/>.</summary>
        public void Select(string stackId, string nodeId)
        {
            _selection.Clear();
            _selection.Add(new BlockRef(stackId, nodeId));
            ApplySelection();
        }

        /// <summary>Adds a block to the selection, keeping what's already selected.</summary>
        public void AddToSelection(string stackId, string nodeId)
        {
            var block = new BlockRef(stackId, nodeId);
            if (_selection.Contains(block)) return;
            _selection.Add(block);
            ApplySelection();
        }

        /// <summary>Shift/Ctrl+click: adds the block, or takes it out if it was selected already.</summary>
        public void ToggleSelected(string stackId, string nodeId)
        {
            var block = new BlockRef(stackId, nodeId);
            if (!_selection.Remove(block)) _selection.Add(block);
            ApplySelection();
        }

        /// <summary>Replaces the selection with <paramref name="blocks"/> (repeats are ignored).</summary>
        public void SetSelection(IReadOnlyList<BlockRef> blocks)
        {
            _selection.Clear();
            foreach (var block in blocks)
                if (!_selection.Contains(block)) _selection.Add(block);
            ApplySelection();
        }

        public bool IsSelected(string stackId, string nodeId) => _selection.Contains(new BlockRef(stackId, nodeId));

        public void ClearSelection()
        {
            _selection.Clear();
            ApplySelection();
        }

        /// <summary>The view showing a block, or a stack's hat when <paramref name="nodeId"/> is null; null if it isn't on the table.</summary>
        public VisualElement FindBlockView(string stackId, string nodeId) => FindBlock(stackId, nodeId);

        /// <summary>
        /// Adds every block on the table whose outline touches <paramref name="worldRect"/> (panel coordinates) to
        /// <paramref name="results"/> — hats, blocks and conditions, nested ones included. Needs a laid-out panel.
        /// </summary>
        public void FindBlocksIn(Rect worldRect, List<BlockRef> results)
        {
            foreach (var stackView in _stackViews.Values)
                if (stackView.parent == this && stackView.Hat != null && stackView.Hat.worldBound.Overlaps(worldRect))
                    results.Add(new BlockRef(stackView.StackId, null));

            foreach (var element in _blocksByNodeId.Values)
                if (element.panel != null && element.worldBound.Overlaps(worldRect) && element is IBlockElement block)
                    results.Add(new BlockRef(block.StackId, block.NodeId));
        }

        /// <summary>
        /// Lights up the blocks scripts are running right now (<see cref="BlockOutline.RunningClass"/>), replacing the
        /// previous set; kept through rebuilds. Call it only when the set changes — it repaints every block it touches.
        /// </summary>
        public void SetRunning(IReadOnlyList<string> nodeIds)
        {
            _runningNodeIds.Clear();
            for (var i = 0; i < nodeIds.Count; i++) _runningNodeIds.Add(nodeIds[i]);
            ApplyRunning();
        }

        /// <summary>
        /// Pins a badge on each block the advice is about — "!" for a problem that stops a script, "?" for a hint —
        /// replacing the previous badges. One badge per block; a problem outranks a hint. A rebuild drops them, so a
        /// host re-applies advice after <see cref="Rebuilt"/>.
        /// </summary>
        public void SetAdvice(IReadOnlyList<Advice> advice)
        {
            foreach (var badge in _adviceBadges) badge.RemoveFromHierarchy();
            _adviceBadges.Clear();

            var badged = new HashSet<VisualElement>();
            AddBadges(advice, AdviceKind.Problem, badged);
            AddBadges(advice, AdviceKind.Hint, badged);
        }

        private void AddBadges(IReadOnlyList<Advice> advice, AdviceKind kind, HashSet<VisualElement> badged)
        {
            foreach (var item in advice)
            {
                if (item.Kind != kind) continue;

                var block = FindBlock(item.StackId, item.NodeId);
                if (block == null || !badged.Add(block)) continue;

                var badge = new Label(kind == AdviceKind.Problem ? "!" : "?") { pickingMode = PickingMode.Ignore };
                badge.AddToClassList(AdviceBadgeClass);
                badge.AddToClassList(kind == AdviceKind.Problem ? "blocky-advice-badge--problem" : "blocky-advice-badge--hint");
                block.Add(badge);
                _adviceBadges.Add(badge);
            }
        }

        /// <summary>The view showing a block, or a stack's hat when <paramref name="nodeId"/> is null; null if it isn't on the table.</summary>
        private VisualElement FindBlock(string stackId, string nodeId)
        {
            if (nodeId != null) return _blocksByNodeId.TryGetValue(nodeId, out var block) ? block : null;
            return stackId != null && _stackViews.TryGetValue(stackId, out var stackView) ? stackView.Hat : null;
        }

        private void Rebuild()
        {
            Clear();
            _stackViews.Clear();
            _blocksByNodeId.Clear();
            _runningElements.Clear();
            _adviceBadges.Clear();

            var simple = _mode == WorkspaceMode.Simple;
            EnableInClassList(SimpleClass, simple);
            _shownRows.Clear();

            foreach (var stack in _store.Program.stacks)
            {
                // Simple mode ignores stored positions — a column has none — so it can't cull by them either.
                if (!simple && _viewport.HasValue && !IntersectsViewport(stack.canvasPosition)) continue;

                var view = new StackView(stack, _registry, _live ? _store : null, _buttons);
                if (simple)
                {
                    view.style.position = Position.Relative; // the column flows: each script under the one before it
                    // A script stretches across the column, so its empty right-hand side would swallow presses meant
                    // for the band behind it. Nothing needs the script itself as a hit target — every block inside it
                    // is picked on its own — so the script steps out of the way and only its blocks are hit.
                    view.pickingMode = PickingMode.Ignore;
                }
                else
                {
                    view.style.position = Position.Absolute;
                    view.style.left = stack.canvasPosition.x;
                    view.style.top = stack.canvasPosition.y;
                }
                // Each script re-measures the column under it, and the bands are drawn from those measurements.
                // Stacks are rebuilt every time, so the callback goes with them — nothing to unregister.
                if (simple) view.RegisterCallback<GeometryChangedEvent>(_onStackLaidOut);
                _stackViews[stack.id] = view;
                Add(view);
            }

            if (simple)
            {
                // The layer and its pooled bands outlive the rebuild: Clear() above dropped it from the hierarchy,
                // not from this field. Putting the same one back keeps the numbers on screen through an edit
                // instead of blanking the gutter until the next layout pass.
                _rowLayer ??= BuildRowLayer();
                Insert(0, _rowLayer); // absolute, so it doesn't join the column — the bands sit behind the blocks
                // The canvas fills the viewport, so its own rect never changes and its GeometryChangedEvent never
                // fires on a rebuild. Measure once this rebuild has been laid out.
                schedule.Execute(UpdateRows);
            }
            else if (_rowLayer != null)
            {
                _rowLayer.RemoveFromHierarchy();
            }

            this.Query<VisualElement>().Where(e => e is IBlockElement { NodeId: not null })
                .ForEach(e => _blocksByNodeId[((IBlockElement)e).NodeId] = e);

            if (_buttons) Add(BuildAddStackRow());
            ApplySelection();
            ApplyRunning();
            Rebuilt?.Invoke();
        }

        /// <summary>
        /// Finds each selected block in the current views and highlights it. Nodes are found by id across every
        /// stack, because a drag can carry a block into a different stack (the stored stack id follows it); a block
        /// that no longer exists drops out of the selection.
        /// </summary>
        private void ApplySelection()
        {
            foreach (var element in _selectedElements)
            {
                element.RemoveFromClassList(BlockOutline.SelectedClass);
                element.MarkDirtyRepaint();
            }
            _selectedElements.Clear();

            for (var i = _selection.Count - 1; i >= 0; i--)
            {
                var selected = _selection[i];
                var found = FindBlock(selected.StackId, selected.NodeId);
                if (found == null)
                {
                    _selection.RemoveAt(i);
                    continue;
                }

                if (found is IBlockElement block && block.StackId != selected.StackId)
                    _selection[i] = new BlockRef(block.StackId, selected.NodeId);
                found.AddToClassList(BlockOutline.SelectedClass);
                found.MarkDirtyRepaint();
                _selectedElements.Add(found);
            }

            ApplyRowSelection();
        }

        private void ApplyRunning()
        {
            foreach (var element in _runningElements)
            {
                element.RemoveFromClassList(BlockOutline.RunningClass);
                element.MarkDirtyRepaint();
            }
            _runningElements.Clear();

            foreach (var nodeId in _runningNodeIds)
            {
                if (!_blocksByNodeId.TryGetValue(nodeId, out var element)) continue;
                element.AddToClassList(BlockOutline.RunningClass);
                element.MarkDirtyRepaint();
                _runningElements.Add(element);
            }
        }

        private VisualElement BuildAddStackRow()
        {
            var container = new VisualElement();
            container.AddToClassList("blocky-block__add-container");

            var popup = new BlockPickerPopup(_registry, def => def.shape == BlockShape.Trigger, def =>
            {
                var existingPositions = new List<Vector2>();
                foreach (var s in _store.Program.stacks) existingPositions.Add(s.canvasPosition);
                var position = StackPlacementResolver.FindFreePosition(Vector2.zero, existingPositions, new Vector2(NominalStackWidth, NominalStackHeight));

                var stack = new BlockStack { id = IdGenerator.NewId(), triggerBlockType = def.blockType, canvasPosition = position };
                _store.Apply(new CreateStack(stack));
            });

            var addButton = new Button(popup.Toggle) { text = "+ Add Stack" };
            addButton.AddToClassList("blocky-block__add-button");

            container.Add(addButton);
            container.Add(popup);
            return container;
        }

        /// <summary>
        /// Numbers the blocks down the column and gives each one a band the height of the block, so the script
        /// reads like the lines of a program and every block has an area you can click (Simple mode only).
        /// Runs after layout: a block's place in the column isn't known until it has been measured. One column
        /// means top-to-bottom order *is* reading order, nested blocks included.
        /// </summary>
        private void UpdateRows()
        {
            if (_rowLayer == null || _rowLayer.parent != this) return; // Free mode: the layer is off the table

            _rows.Clear();
            this.Query<VisualElement>().Where(e => e is BlockView or HatView).ForEach(CollectRow);
            _rows.Sort(_inReadingOrder);

            _shownRows.Clear();
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = RowAt(i);
                row.Set(_rows[i].Block, i + 1, _rows[i].Y, _rows[i].Height);
                _shownRows.Add(row);
            }
            for (var i = _rows.Count; i < _rowPool.Count; i++) _rowPool[i].Hide();
            ApplyRowSelection();
        }

        private void CollectRow(VisualElement element)
        {
            var y = CanvasY(element);
            var height = element.layout.height;
            if (float.IsNaN(y) || float.IsNaN(height)) return;
            _rows.Add(new RowBand(y, height, new BlockRef(((IBlockElement)element).StackId, ((IBlockElement)element).NodeId)));
        }

        /// <summary>Tints the band of every selected block, so a pick shows across the whole line and not just on the block.</summary>
        private void ApplyRowSelection()
        {
            foreach (var row in _shownRows)
                row.EnableInClassList(CanvasRow.SelectedUssClassName, _selection.Contains(row.Block));
        }

        /// <summary>A press anywhere on a block's band picks that block — the row *is* the block, as far as choosing goes.</summary>
        private void OnRowPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.currentTarget is not CanvasRow row || row.Block.StackId == null) return;
            Select(row.Block.StackId, row.Block.NodeId);
            // Deliberately not stopped: the table still starts its scroll from here, so a band can be dragged to
            // move up and down the column exactly like the empty space around it.
        }

        /// <summary>A block's top edge in canvas coordinates, walked up through its parents — no world transform needed.</summary>
        private float CanvasY(VisualElement element)
        {
            var y = 0f;
            for (var el = element; el != null && el != this; el = el.parent) y += el.layout.y;
            return y;
        }

        private VisualElement BuildRowLayer()
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore }; // only the bands inside it are clickable
            layer.AddToClassList(RowLayerClass);
            return layer;
        }

        private CanvasRow RowAt(int index)
        {
            if (index < _rowPool.Count) return _rowPool[index];

            var row = new CanvasRow();
            row.RegisterCallback<PointerDownEvent>(OnRowPointerDown);
            _rowLayer.Add(row);
            _rowPool.Add(row);
            return row;
        }

        private bool IntersectsViewport(Vector2 canvasPosition)
        {
            var v = _viewport.Value;
            var expanded = new Rect(v.x - ViewportMargin, v.y - ViewportMargin, v.width + ViewportMargin * 2, v.height + ViewportMargin * 2);
            var stackRect = new Rect(canvasPosition.x, canvasPosition.y, NominalStackWidth, NominalStackHeight);
            return expanded.Overlaps(stackRect);
        }
    }
}
