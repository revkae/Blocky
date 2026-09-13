using System.Collections.Generic;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    public enum SnapKind
    {
        Below,      // the chain's top connects under a block, or under a hat
        MouthTop,   // the chain's top connects at the top of a C-block's mouth
        AboveStack  // the chain's bottom connects onto a loose stack's first block
    }

    /// <summary>One connection point on the table, in panel (world) space.</summary>
    public readonly struct SnapTarget
    {
        public readonly SnapKind Kind;
        public readonly Vector2 Point;
        public readonly string StackId;
        public readonly NodeLocation InsertAt; // Below / MouthTop
        public readonly bool HasFollowers;      // blocks already sit after the insertion point
        public readonly Rect Edge;              // the edge that glows while this is the chosen target

        public SnapTarget(SnapKind kind, Vector2 point, string stackId, NodeLocation insertAt, bool hasFollowers, Rect edge)
        {
            Kind = kind;
            Point = point;
            StackId = stackId;
            InsertAt = insertAt;
            HasFollowers = hasFollowers;
            Edge = edge;
        }
    }

    /// <summary>The chain being dragged: where its top and bottom connectors are now, and what its shape allows.</summary>
    public readonly struct DraggedChain
    {
        public readonly Vector2 Top;
        public readonly Vector2 Bottom;
        public readonly bool HasHat;
        public readonly bool EndsWithCap;

        public DraggedChain(Vector2 top, Vector2 bottom, bool hasHat, bool endsWithCap)
        {
            Top = top;
            Bottom = bottom;
            HasHat = hasHat;
            EndsWithCap = endsWithCap;
        }
    }

    /// <summary>
    /// Scratch-style proximity snapping: a connector on the dragged chain within <c>radius</c> of a matching
    /// connector on the table snaps, and the nearest wins. The rules are the ones the silhouettes draw: a hat has
    /// no top connector, so it can only attach its bottom above a loose stack; a cap has no bottom connector, so
    /// it can't attach above anything and can't be inserted where blocks already follow.
    /// </summary>
    public static class SnapResolver
    {
        public const float DefaultRadius = 32f;

        public static SnapTarget? FindBest(IReadOnlyList<SnapTarget> targets, DraggedChain chain, float radius = DefaultRadius)
        {
            SnapTarget? best = null;
            var bestDistance = radius;

            foreach (var target in targets)
            {
                float distance;
                if (target.Kind == SnapKind.AboveStack)
                {
                    if (chain.EndsWithCap) continue;
                    distance = Vector2.Distance(chain.Bottom, target.Point);
                }
                else
                {
                    if (chain.HasHat) continue;
                    if (chain.EndsWithCap && target.HasFollowers) continue;
                    distance = Vector2.Distance(chain.Top, target.Point);
                }

                if (distance > bestDistance) continue;
                best = target;
                bestDistance = distance;
            }

            return best;
        }
    }

    /// <summary>A block's condition slot on the table, in panel (world) space — somewhere a dragged condition can go.</summary>
    public readonly struct ConditionSlotTarget
    {
        public readonly string StackId;
        public readonly string OwnerNodeId;
        public readonly string ParamKey;
        public readonly Rect Bounds;

        public ConditionSlotTarget(string stackId, string ownerNodeId, string paramKey, Rect bounds)
        {
            StackId = stackId;
            OwnerNodeId = ownerNodeId;
            ParamKey = paramKey;
            Bounds = bounds;
        }
    }

    /// <summary>
    /// Where a dragged condition snaps: the slot nearest the condition's left point (its leading tip, the part
    /// you aim with), within <c>radius</c> of the slot's outline. Filled slots count too — dropping there swaps.
    /// On a tie (the tip inside two slots, one within the other) the smaller, innermost slot wins.
    /// </summary>
    public static class ConditionSlotResolver
    {
        public const float DefaultRadius = 28f;

        public static ConditionSlotTarget? FindBest(IReadOnlyList<ConditionSlotTarget> slots, Vector2 tip, float radius = DefaultRadius)
        {
            ConditionSlotTarget? best = null;
            var bestDistance = radius;
            var bestArea = float.MaxValue;

            foreach (var slot in slots)
            {
                var distance = DistanceToRect(tip, slot.Bounds);
                var area = slot.Bounds.width * slot.Bounds.height;
                if (distance > bestDistance || (Mathf.Approximately(distance, bestDistance) && area >= bestArea)) continue;
                best = slot;
                bestDistance = distance;
                bestArea = area;
            }

            return best;
        }

        private static float DistanceToRect(Vector2 point, Rect rect)
        {
            var dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
            var dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Every condition slot on the table. Slots inside a chain being dragged sit in the drag layer, not the canvas, so they never appear.</summary>
        public static List<ConditionSlotTarget> Collect(ProgramCanvasView canvas)
        {
            var targets = new List<ConditionSlotTarget>();
            foreach (var slot in canvas.Query<ConditionSlot>().ToList())
                if (slot.OwnerNodeId != null)
                    targets.Add(new ConditionSlotTarget(slot.StackId, slot.OwnerNodeId, slot.ParamKey, slot.worldBound));
            return targets;
        }
    }

    /// <summary>
    /// Walks the live canvas and lists every connection point. Elements being dragged are re-parented into the
    /// drag layer for the duration, so they are naturally absent — a chain can never snap onto itself.
    /// </summary>
    public static class SnapTargetCollector
    {
        private const float EdgeThickness = 6f;
        private const float MinEdgeWidth = 60f;

        public static List<SnapTarget> Collect(ProgramCanvasView canvas)
        {
            var targets = new List<SnapTarget>();

            foreach (var stackView in canvas.StackViews.Values)
            {
                if (stackView.parent != canvas) continue; // this whole stack is the one being dragged

                var blocks = BlockChildren(stackView.SequenceContainer);
                if (stackView.Hat != null)
                {
                    var hat = stackView.Hat.worldBound;
                    targets.Add(new SnapTarget(SnapKind.Below, new Vector2(hat.x, hat.yMax), stackView.StackId,
                        NodeLocation.InStack(stackView.StackId, 0), blocks.Count > 0, BottomEdge(hat)));
                }
                else if (blocks.Count > 0)
                {
                    var first = blocks[0].worldBound;
                    targets.Add(new SnapTarget(SnapKind.AboveStack, first.position, stackView.StackId, default, true, TopEdge(first)));
                }

                CollectSequence(blocks, stackView.StackId, null, -1, targets);
            }

            return targets;
        }

        private static void CollectSequence(List<VisualElement> blocks, string stackId, string parentNodeId, int branchIndex, List<SnapTarget> targets)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                var rect = blocks[i].worldBound;
                var blockView = blocks[i] as BlockView;

                if (blockView == null || !blockView.IsTerminal)
                    targets.Add(new SnapTarget(SnapKind.Below, new Vector2(rect.x, rect.yMax), stackId,
                        new NodeLocation(stackId, parentNodeId, branchIndex, i + 1), i + 1 < blocks.Count, BottomEdge(rect)));

                if (blockView == null) continue;
                for (var b = 0; b < blockView.BodySlots.Count; b++)
                {
                    var slot = blockView.BodySlots[b];
                    var inner = BlockChildren(slot);
                    var slotRect = slot.worldBound;
                    targets.Add(new SnapTarget(SnapKind.MouthTop, slotRect.position, stackId,
                        NodeLocation.InBranch(stackId, blockView.NodeId, b, 0), inner.Count > 0, TopEdge(slotRect)));
                    CollectSequence(inner, stackId, blockView.NodeId, b, targets);
                }
            }
        }

        private static List<VisualElement> BlockChildren(VisualElement container)
        {
            var result = new List<VisualElement>();
            foreach (var child in container.Children())
                if (child is BlockView || child is UnknownBlockView) result.Add(child);
            return result;
        }

        private static Rect BottomEdge(Rect r) => new(r.x, r.yMax - EdgeThickness / 2f, Mathf.Max(r.width, MinEdgeWidth), EdgeThickness);

        private static Rect TopEdge(Rect r) => new(r.x, r.y - EdgeThickness / 2f, Mathf.Max(r.width, MinEdgeWidth), EdgeThickness);
    }
}
