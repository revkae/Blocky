using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Hosts every stack, positioned by <c>canvasPosition</c>, and rebuilds whenever
    /// <see cref="ProgramStore.OnChanged"/> fires (TDD §8.2). Rebuilds the whole canvas on any change rather
    /// than patching the affected subtree — correct and simple; subtree patching is an optimization.
    /// Modes: read-only (default); <c>editable</c> — live fields plus the Editor window's add/delete buttons;
    /// <c>tableMode</c> — live fields and no buttons at all: blocks are placed, moved, and deleted purely by drag
    /// and drop (the in-game workspace attaches the drag manipulators on <see cref="Rebuilt"/>).
    /// Also owns which block is selected, so the highlight survives the rebuild every edit causes.
    /// </summary>
    public sealed class ProgramCanvasView : VisualElement
    {
        // A stack's real footprint depends on its content, which we don't know without a live panel — this is a
        // deliberately generous nominal size so virtualization never hides a stack that would be partly visible.
        private const float NominalStackWidth = 240f;
        private const float NominalStackHeight = 160f;
        private const float ViewportMargin = 200f;

        private readonly ProgramStore _store;
        private readonly BlockRegistry _registry;
        private readonly bool _live;
        private readonly bool _buttons;
        private readonly Dictionary<string, StackView> _stackViews = new();
        private Rect? _viewport;

        private string _selectedStackId;
        private string _selectedNodeId; // null together with a stack id = that stack's hat
        private VisualElement _selectedElement;

        public IReadOnlyDictionary<string, StackView> StackViews => _stackViews;

        /// <summary>Raised after every rebuild, so a host can decorate the fresh block views (e.g. attach drag manipulators).</summary>
        public event Action Rebuilt;

        public bool HasSelection => _selectedStackId != null;
        public string SelectedStackId => _selectedStackId;
        public string SelectedNodeId => _selectedNodeId;

        public ProgramCanvasView(ProgramStore store, BlockRegistry registry, bool editable = false, bool tableMode = false)
        {
            _store = store;
            _registry = registry;
            _live = editable || tableMode;
            _buttons = editable && !tableMode;
            AddToClassList("blocky-canvas");
            style.position = Position.Relative;

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

        /// <summary>Selects a block (<paramref name="nodeId"/>) or, with a null node id, the hat of <paramref name="stackId"/>.</summary>
        public void Select(string stackId, string nodeId)
        {
            _selectedStackId = stackId;
            _selectedNodeId = nodeId;
            ApplySelection();
        }

        public void ClearSelection()
        {
            _selectedStackId = null;
            _selectedNodeId = null;
            ApplySelection();
        }

        private void Rebuild()
        {
            Clear();
            _stackViews.Clear();

            foreach (var stack in _store.Program.stacks)
            {
                if (_viewport.HasValue && !IntersectsViewport(stack.canvasPosition)) continue;

                var view = new StackView(stack, _registry, _live ? _store : null, _buttons);
                view.style.position = Position.Absolute;
                view.style.left = stack.canvasPosition.x;
                view.style.top = stack.canvasPosition.y;
                _stackViews[stack.id] = view;
                Add(view);
            }

            if (_buttons) Add(BuildAddStackRow());
            ApplySelection();
            Rebuilt?.Invoke();
        }

        /// <summary>
        /// Finds the selected block in the current views and highlights it. Nodes are found by id across every
        /// stack, because a drag can carry a block into a different stack; if it no longer exists, the selection clears.
        /// </summary>
        private void ApplySelection()
        {
            if (_selectedElement != null)
            {
                _selectedElement.RemoveFromClassList(BlockOutline.SelectedClass);
                _selectedElement.MarkDirtyRepaint();
                _selectedElement = null;
            }

            if (_selectedStackId == null) return;

            VisualElement found = null;
            if (_selectedNodeId == null)
            {
                if (_stackViews.TryGetValue(_selectedStackId, out var stackView)) found = stackView.Hat;
            }
            else
            {
                foreach (var stackView in _stackViews.Values)
                {
                    var block = stackView.Query<BlockView>().Where(b => b.NodeId == _selectedNodeId).First();
                    if (block != null)
                    {
                        found = block;
                        _selectedStackId = block.StackId;
                        break;
                    }

                    var condition = stackView.Query<ConditionView>().Where(c => c.NodeId == _selectedNodeId).First();
                    if (condition != null)
                    {
                        found = condition;
                        _selectedStackId = condition.StackId;
                        break;
                    }
                }
            }

            if (found == null)
            {
                _selectedStackId = null;
                _selectedNodeId = null;
                return;
            }

            found.AddToClassList(BlockOutline.SelectedClass);
            found.MarkDirtyRepaint();
            _selectedElement = found;
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

        private bool IntersectsViewport(Vector2 canvasPosition)
        {
            var v = _viewport.Value;
            var expanded = new Rect(v.x - ViewportMargin, v.y - ViewportMargin, v.width + ViewportMargin * 2, v.height + ViewportMargin * 2);
            var stackRect = new Rect(canvasPosition.x, canvasPosition.y, NominalStackWidth, NominalStackHeight);
            return expanded.Overlaps(stackRect);
        }
    }
}
