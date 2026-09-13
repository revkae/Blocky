using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Editor
{
    /// <summary>
    /// Hit-testing over a candidate list already collected for the current drag (TDD §8.3). Ties resolve
    /// innermost-first: among every candidate whose rect contains the pointer, the smallest-area one wins.
    /// </summary>
    public static class DropCandidateResolver
    {
        public static DropCandidate? FindBestCandidate(IReadOnlyList<DropCandidate> candidates, Vector2 pointerPosition)
        {
            DropCandidate? best = null;
            var bestArea = float.PositiveInfinity;

            foreach (var candidate in candidates)
            {
                if (!candidate.Rect.Contains(pointerPosition)) continue;
                var area = candidate.Rect.width * candidate.Rect.height;
                if (area >= bestArea) continue;
                best = candidate;
                bestArea = area;
            }

            return best;
        }
    }
}
