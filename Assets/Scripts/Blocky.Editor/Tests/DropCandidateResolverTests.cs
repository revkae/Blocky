using NUnit.Framework;
using UnityEngine;

namespace Blocky.Editor.Tests
{
    public class DropCandidateResolverTests
    {
        private static DropCandidate Candidate(Rect rect) => new(rect, DropCandidateKind.SequenceGap, "stk", null, -1, 0, 0);

        [Test]
        public void PicksSmallestContainingRect_SoNestedCavityWinsOverOuterOne()
        {
            var outer = Candidate(new Rect(0, 0, 200, 200));
            var inner = Candidate(new Rect(50, 50, 20, 20));

            var best = DropCandidateResolver.FindBestCandidate(new[] { outer, inner }, new Vector2(55, 55));

            Assert.AreEqual(inner.Rect, best.Value.Rect);
        }

        [Test]
        public void ReturnsNull_WhenNoCandidateContainsThePoint()
        {
            var candidate = Candidate(new Rect(0, 0, 10, 10));
            var best = DropCandidateResolver.FindBestCandidate(new[] { candidate }, new Vector2(500, 500));
            Assert.IsNull(best);
        }
    }
}
