using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Hosts every top-level stack, positioned by <c>canvasPosition</c>, and rebuilds whenever
    /// <see cref="ProgramStore.OnChanged"/> fires (TDD §8.2). Rebuilds the whole canvas on any change rather
    /// than patching just the affected subtree — correct and simple; per-subtree patching using the
    /// <c>StructureChange</c> diff is a later optimization, not a correctness requirement. Interactive pan/zoom
    /// is not implemented yet (out of scope for "first end-to-end authored-and-run behaviour").
    /// Pass <paramref name="editable"/> true to get live fields and add/delete affordances (the authoring window).
    /// </summary>
    public sealed class ProgramCanvasView : VisualElement
    {
        // A stack's real footprint depends on its content (Milestone 4's flex layout), which we don't know
        // without a live panel — this is a deliberately generous nominal size so virtualization never hides a
        // stack that would actually be partly visible, only ones clearly off-screen (TDD §8.4 "huge programs").
        private const float NominalStackWidth = 240f;
        private const float NominalStackHeight = 160f;
        private const float ViewportMargin = 200f;

        private readonly ProgramStore _store;
        private readonly BlockRegistry _registry;
        private readonly bool _editable;
        private readonly Dictionary<string, StackView> _stackViews = new();
        private Rect? _viewport;

        public IReadOnlyDictionary<string, StackView> StackViews => _stackViews;

        public ProgramCanvasView(ProgramStore store, BlockRegistry registry, bool editable = false)
        {
            _store = store;
            _registry = registry;
            _editable = editable;
            AddToClassList("blocky-canvas");
            style.position = Position.Relative;

            Rebuild();
            _store.OnChanged += _ => Rebuild();
        }

        /// <summary>
        /// Sets the visible viewport in canvas space; stacks entirely outside it (plus a margin) are not
        /// instantiated at all (TDD §8.4). Pass <c>null</c> to disable virtualization and render every stack —
        /// the default until something actually drives pan/zoom and calls this.
        /// </summary>
        public void SetViewport(Rect? viewport)
        {
            _viewport = viewport;
            Rebuild();
        }

        private void Rebuild()
        {
            Clear();
            _stackViews.Clear();

            foreach (var stack in _store.Program.stacks)
            {
                if (_viewport.HasValue && !IntersectsViewport(stack.canvasPosition)) continue;

                var view = new StackView(stack, _registry, _editable ? _store : null);
                view.style.position = Position.Absolute;
                view.style.left = stack.canvasPosition.x;
                view.style.top = stack.canvasPosition.y;
                _stackViews[stack.id] = view;
                Add(view);
            }

            if (_editable) Add(BuildAddStackRow());
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
