using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Collected once at drag start, rebuilt only on structural change, not per pointer move (TDD §8.3). Rect
    /// lookup is injected so this can be exercised without a live panel — production code defaults to
    /// <c>VisualElement.worldBound</c>, which only returns meaningful rects once the tree is attached to a panel.
    /// </summary>
    public static class DropCandidateBuilder
    {
        private const float GapBandHeight = 8f;

        public static List<DropCandidate> Build(StackView stack, Func<VisualElement, Rect> rectOf = null)
        {
            rectOf ??= el => el.worldBound;
            var candidates = new List<DropCandidate>();
            BuildSequenceCandidates(stack.SequenceContainer, stack.StackId, null, -1, 0, rectOf, candidates);
            return candidates;
        }

        private static void BuildSequenceCandidates(VisualElement sequenceContainer, string stackId, string parentNodeId,
            int branchIndex, int depth, Func<VisualElement, Rect> rectOf, List<DropCandidate> candidates)
        {
            var blockViews = new List<BlockView>();
            foreach (var child in sequenceContainer.Children())
                if (child is BlockView blockView) blockViews.Add(blockView);

            if (blockViews.Count == 0)
            {
                candidates.Add(new DropCandidate(rectOf(sequenceContainer), DropCandidateKind.SequenceEnd, stackId, parentNodeId, branchIndex, 0, depth));
                return;
            }

            for (var i = 0; i < blockViews.Count; i++)
            {
                var blockRect = rectOf(blockViews[i]);
                var gapRect = new Rect(blockRect.x, blockRect.y - GapBandHeight / 2f, blockRect.width, GapBandHeight);
                candidates.Add(new DropCandidate(gapRect, DropCandidateKind.SequenceGap, stackId, parentNodeId, branchIndex, i, depth));

                for (var slotIndex = 0; slotIndex < blockViews[i].BodySlots.Count; slotIndex++)
                    BuildBodySlotCandidates(blockViews[i].BodySlots[slotIndex], stackId, blockViews[i].NodeId, slotIndex, depth + 1, rectOf, candidates);
            }

            var lastRect = rectOf(blockViews[^1]);
            var endRect = new Rect(lastRect.x, lastRect.yMax - GapBandHeight / 2f, lastRect.width, GapBandHeight);
            candidates.Add(new DropCandidate(endRect, DropCandidateKind.SequenceEnd, stackId, parentNodeId, branchIndex, blockViews.Count, depth));
        }

        private static void BuildBodySlotCandidates(VisualElement slot, string stackId, string parentNodeId, int branchIndex,
            int depth, Func<VisualElement, Rect> rectOf, List<DropCandidate> candidates)
        {
            var hasChildren = false;
            foreach (var _ in slot.Children()) { hasChildren = true; break; }

            if (!hasChildren)
            {
                candidates.Add(new DropCandidate(rectOf(slot), DropCandidateKind.BodyCavity, stackId, parentNodeId, branchIndex, 0, depth));
                return;
            }

            BuildSequenceCandidates(slot, stackId, parentNodeId, branchIndex, depth, rectOf, candidates);
        }
    }
}
