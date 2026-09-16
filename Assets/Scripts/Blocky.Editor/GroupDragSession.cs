using System;
using System.Collections.Generic;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Several selected blocks dragged together (picked with Shift/Ctrl+click or a selection box). Each one is
    /// lifted the way a single drag would lift it — a hat or the first block of a loose stack takes its stack, a
    /// block takes the blocks below it, a condition leaves its slot — and they keep their places relative to each
    /// other while they follow the pointer. On release every piece lands loose, moved by the same amount; dropped
    /// on the palette they're all deleted. Groups never snap: there's no one edge a group could connect by.
    /// The whole drop is one <see cref="MoveBlocks"/> command, so one Undo takes it back.
    /// </summary>
    public sealed class GroupDragSession : IDragSession
    {
        private readonly DragContext _context;
        private readonly VisualElement _ghost;
        private readonly Vector2 _startPointer;
        private readonly float _zoom;
        private readonly List<BlockRef> _blocks;
        private readonly List<Vector2> _startPositions;

        private GroupDragSession(DragContext context, VisualElement ghost, Vector2 startPointer, float zoom,
            List<BlockRef> blocks, List<Vector2> startPositions)
        {
            _context = context;
            _ghost = ghost;
            _startPointer = startPointer;
            _zoom = zoom;
            _blocks = blocks;
            _startPositions = startPositions;
        }

        /// <summary>Lifts the selected blocks off the table into one ghost; null when none of them is on the table.</summary>
        /// <param name="pointer">Where the drag started, in panel coordinates.</param>
        public static GroupDragSession Start(DragContext context, IReadOnlyList<BlockRef> selection, Vector2 pointer)
        {
            var canvas = context.Canvas;
            var zoom = Mathf.Max(context.CanvasZoom(), 0.01f);

            // Measure every piece before lifting any: lifting one re-flows the table under the others.
            var pieces = new List<(VisualElement lifted, List<VisualElement> tail, Vector2 world)>();
            var blocks = new List<BlockRef>();
            var positions = new List<Vector2>();
            foreach (var block in MoveBlocks.Outermost(context.Store.Program, selection))
            {
                var view = canvas.FindBlockView(block.StackId, block.NodeId);
                var stackView = view?.GetFirstAncestorOfType<StackView>();
                if (stackView == null) continue;

                VisualElement lifted;
                List<VisualElement> tail = null;
                if (view is ConditionView { IsInSlot: true })
                {
                    lifted = view;
                }
                else if (view is HatView || IsFirstBlockOfLooseStack(stackView, view))
                {
                    lifted = stackView;
                }
                else
                {
                    lifted = view;
                    var siblings = SnapTargetCollector.BlockChildren(view.parent);
                    var start = siblings.IndexOf(view);
                    tail = siblings.GetRange(start, siblings.Count - start); // Scratch's rule: the blocks below come too
                }

                var world = lifted.worldBound.position;
                pieces.Add((lifted, tail, world));
                blocks.Add(block);
                positions.Add(canvas.WorldToLocal(world));
            }
            if (pieces.Count == 0) return null;

            // One ghost, drawn at the table's zoom; each piece sits where it was relative to the pointer.
            var ghost = new VisualElement();
            ghost.AddToClassList("blocky-drag-ghost");
            ghost.style.position = Position.Absolute;
            ghost.style.transformOrigin = new TransformOrigin(new Length(0f), new Length(0f), 0f);
            ghost.style.scale = new Scale(new Vector3(zoom, zoom, 1f));

            foreach (var (lifted, tail, world) in pieces)
            {
                var piece = lifted;
                if (tail != null)
                {
                    piece = new VisualElement();
                    piece.AddToClassList("blocky-drag-chain");
                    foreach (var element in tail) piece.Add(element); // Add re-parents out of the table
                }

                var offset = (world - pointer) / zoom;
                piece.style.position = Position.Absolute;
                piece.style.left = offset.x;
                piece.style.top = offset.y;
                ghost.Add(piece);
            }

            ChainDragSession.IgnorePicking(ghost); // the ghost sits under the pointer — it must never become the hit target
            context.DragLayer.Add(ghost);

            var session = new GroupDragSession(context, ghost, pointer, zoom, blocks, positions);
            session.Move(pointer);
            return session;
        }

        public void Move(Vector2 pointer)
        {
            var local = _context.DragLayer.WorldToLocal(pointer);
            _ghost.style.left = local.x;
            _ghost.style.top = local.y;
        }

        public void Drop(Vector2 pointer)
        {
            _ghost.RemoveFromHierarchy();

            IProgramCommand command;
            if (_context.IsOverDiscard(pointer)) command = MoveBlocks.Discard(_blocks);
            else if (_context.IsOverCanvas(pointer)) command = MoveBlocks.By(_blocks, _startPositions, (pointer - _startPointer) / _zoom);
            else
            {
                _context.Canvas.Refresh(); // dropped outside the workspace: put everything back
                return;
            }

            try
            {
                _context.Store.Apply(command);
            }
            catch (Exception e)
            {
                // The command restores the program on failure; rebuild so the table matches it again.
                Debug.LogException(e);
                _context.Canvas.Refresh();
            }
        }

        public void Cancel()
        {
            _ghost.RemoveFromHierarchy();
            _context.Canvas.Refresh();
        }

        private static bool IsFirstBlockOfLooseStack(StackView stackView, VisualElement view) =>
            stackView.Hat == null && stackView.SequenceContainer.childCount > 0 && stackView.SequenceContainer.ElementAt(0) == view;
    }
}
