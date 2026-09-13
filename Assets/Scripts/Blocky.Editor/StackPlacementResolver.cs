using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Editor
{
    /// <summary>Nudges a desired canvas position by (16, 16) repeatedly until it clears every existing stack's nominal rect (TDD §8.4).</summary>
    public static class StackPlacementResolver
    {
        private static readonly Vector2 NudgeStep = new(16f, 16f);
        private const int MaxNudges = 256; // generous upper bound; a real canvas will never need this many

        public static Vector2 FindFreePosition(Vector2 desired, IReadOnlyList<Vector2> existingPositions, Vector2 stackSize)
        {
            var candidate = desired;
            for (var i = 0; i < MaxNudges; i++)
            {
                if (!CollidesWithAny(candidate, existingPositions, stackSize)) return candidate;
                candidate += NudgeStep;
            }
            return candidate; // give up nudging further; caller still gets a position, just possibly still overlapping
        }

        private static bool CollidesWithAny(Vector2 position, IReadOnlyList<Vector2> existingPositions, Vector2 stackSize)
        {
            var rect = new Rect(position, stackSize);
            foreach (var existing in existingPositions)
                if (rect.Overlaps(new Rect(existing, stackSize))) return true;
            return false;
        }
    }
}
