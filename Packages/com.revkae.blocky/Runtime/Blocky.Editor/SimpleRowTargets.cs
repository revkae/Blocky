using System.Collections.Generic;
using Blocky.Data;
using UnityEngine;
using UnityEngine.UIElements;

// The statics here are fixed values that never change, so there is nothing to reset between Play sessions.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Editor
{
    /// <summary>What a drop on a block's row does to the script (<see cref="WorkspaceMode.Simple"/>).</summary>
    public enum RowIntent
    {
        /// <summary>The chain goes in above the block, pushing it down.</summary>
        Above,

        /// <summary>The chain takes the block's place; that block, and anything nested in it, goes.</summary>
        Replace,

        /// <summary>The chain goes in below the block.</summary>
        Below
    }

    /// <summary>
    /// One numbered row on the strict column — a block, and the three places a dragged chain can land on it.
    /// Bounds are in panel (world) space and span the block itself; the band drawn behind it is full width, so
    /// only the vertical extent is ever tested.
    /// </summary>
    public readonly struct RowTarget
    {
        /// <summary>The block's own slot in its container: insert here and the chain lands above the block.</summary>
        public readonly NodeLocation At;

        public readonly string StackId;

        /// <summary>The block this row is, or null when the row is a stack's hat.</summary>
        public readonly string NodeId;

        public readonly Rect Bounds;

        /// <summary>Blocks already follow this one in its container — a chain ending in a cap can't go in front of them.</summary>
        public readonly bool HasFollowers;

        /// <summary>Nothing can attach under this block (a cap, e.g. forever).</summary>
        public readonly bool IsTerminal;

        /// <summary>A hat: a chain can only go under it, never above it and never in its place.</summary>
        public bool IsHat => NodeId == null;

        public RowTarget(NodeLocation at, string stackId, string nodeId, Rect bounds, bool hasFollowers, bool isTerminal)
        {
            At = at;
            StackId = stackId;
            NodeId = nodeId;
            Bounds = bounds;
            HasFollowers = hasFollowers;
            IsTerminal = isTerminal;
        }

        /// <summary>Where the chain ends up. Above and Replace use the block's own slot; Below is the next one along.</summary>
        public ChainTarget Resolve(RowIntent intent) => intent switch
        {
            RowIntent.Above => ChainTarget.Insert(At),
            RowIntent.Replace => ChainTarget.Replace(At),
            _ => ChainTarget.Insert(new NodeLocation(At.StackId, At.ParentNodeId, At.BranchIndex, At.Index + 1))
        };

        /// <summary>
        /// The shape rules, the same ones the snap silhouettes draw: nothing goes above or in place of a hat;
        /// nothing goes under a cap; and a chain that ends in a cap cannot be put in front of blocks that follow,
        /// because nothing could ever attach under it again.
        /// </summary>
        public bool Allows(RowIntent intent, bool chainEndsWithCap) => intent switch
        {
            RowIntent.Above => !IsHat && !chainEndsWithCap,
            RowIntent.Replace => !IsHat && !(chainEndsWithCap && HasFollowers),
            _ => !IsTerminal && !(chainEndsWithCap && HasFollowers)
        };
    }

    /// <summary>
    /// Which row a drop is over, and which of its three places it means. The row is picked by the pointer's
    /// height alone — the band spans the whole width — and the innermost one wins, so a block nested in a
    /// C-block's mouth is aimed at rather than the C-block around it.
    /// Near the top or bottom edge means above or below; the middle of the row means "put it here instead".
    /// The edge zone is a fraction of short rows and a fixed band on tall ones, so a C-block holding half a
    /// script does not turn most of the table into "above".
    /// </summary>
    public static class RowTargetResolver
    {
        public const float EdgeFraction = 0.3f;
        public const float MaxEdgePixels = 26f;

        /// <summary>The three intents in the order a disallowed one falls back through.</summary>
        private static readonly RowIntent[] Fallbacks = { RowIntent.Below, RowIntent.Above, RowIntent.Replace };

        public static bool TryFind(IReadOnlyList<RowTarget> rows, Vector2 pointer, bool chainEndsWithCap,
            out RowTarget row, out RowIntent intent)
        {
            row = default;
            intent = RowIntent.Below;

            var found = false;
            var bestHeight = float.MaxValue;
            foreach (var candidate in rows)
            {
                if (pointer.y < candidate.Bounds.yMin || pointer.y > candidate.Bounds.yMax) continue;
                if (candidate.Bounds.height >= bestHeight) continue; // the innermost row is the smallest one holding the pointer
                bestHeight = candidate.Bounds.height;
                row = candidate;
                found = true;
            }
            if (!found) return false;

            intent = IntentFor(row, pointer.y);
            if (row.Allows(intent, chainEndsWithCap)) return true;

            foreach (var fallback in Fallbacks)
                if (row.Allows(fallback, chainEndsWithCap))
                {
                    intent = fallback;
                    return true;
                }

            return false; // this row refuses every intent: the drop falls back to the end of the nearest script
        }

        private static RowIntent IntentFor(RowTarget row, float y)
        {
            var edge = Mathf.Min(row.Bounds.height * EdgeFraction, MaxEdgePixels);
            if (y <= row.Bounds.yMin + edge) return RowIntent.Above;
            if (y >= row.Bounds.yMax - edge) return RowIntent.Below;
            return RowIntent.Replace;
        }
    }

    /// <summary>
    /// Walks the live canvas and lists one row per block, the way <see cref="SnapTargetCollector"/> lists
    /// connection points — and for the same reason the indices line up: the blocks being dragged have been
    /// re-parented into the drag layer, so they are absent here exactly as they will be absent from the
    /// program once the drop picks them up.
    /// </summary>
    public static class RowTargetCollector
    {
        /// <summary>Fills <paramref name="rows"/> (cleared first) — lets a drag reuse one list across refreshes.</summary>
        public static void Collect(ProgramCanvasView canvas, List<RowTarget> rows)
        {
            rows.Clear();

            foreach (var stackView in canvas.StackViews.Values)
            {
                if (stackView.parent != canvas) continue; // this whole stack is the one being dragged

                var blocks = SnapTargetCollector.BlockChildren(stackView.SequenceContainer);
                if (stackView.Hat != null)
                    rows.Add(new RowTarget(NodeLocation.InStack(stackView.StackId, 0), stackView.StackId, null,
                        stackView.Hat.worldBound, blocks.Count > 0, isTerminal: false));

                CollectSequence(blocks, stackView.StackId, null, -1, rows);
            }
        }

        private static void CollectSequence(List<VisualElement> blocks, string stackId, string parentNodeId, int branchIndex, List<RowTarget> rows)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                var blockView = blocks[i] as BlockView;
                var nodeId = (blocks[i] as IBlockElement)?.NodeId;
                if (nodeId == null) continue; // an unknown block: nothing sound to say about where it goes

                rows.Add(new RowTarget(new NodeLocation(stackId, parentNodeId, branchIndex, i), stackId, nodeId,
                    blocks[i].worldBound, i + 1 < blocks.Count, blockView is { IsTerminal: true }));

                if (blockView == null) continue;
                for (var b = 0; b < blockView.BodySlots.Count; b++)
                    CollectSequence(SnapTargetCollector.BlockChildren(blockView.BodySlots[b]), stackId, blockView.NodeId, b, rows);
            }
        }
    }
}
