using System.Collections.Generic;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Editor.Tests
{
    public class SnapResolverTests
    {
        private static SnapTarget Below(Vector2 point, bool hasFollowers = false) =>
            new(SnapKind.Below, point, "stk", NodeLocation.InStack("stk", 1), hasFollowers, default);

        private static SnapTarget Above(Vector2 point) =>
            new(SnapKind.AboveStack, point, "stk_loose", default, true, default);

        private static DraggedChain Chain(Vector2 top, float height = 30f, bool hat = false, bool cap = false) =>
            new(top, top + new Vector2(0f, height), hat, cap);

        [Test]
        public void NearestTargetWithinRadius_Wins()
        {
            var targets = new List<SnapTarget> { Below(new Vector2(100, 100)), Below(new Vector2(100, 110)) };

            var best = SnapResolver.FindBest(targets, Chain(new Vector2(102, 108)));

            Assert.AreEqual(110f, best.Value.Point.y);
        }

        [Test]
        public void NothingWithinRadius_ReturnsNull()
        {
            var best = SnapResolver.FindBest(new List<SnapTarget> { Below(Vector2.zero) }, Chain(new Vector2(500, 500)));

            Assert.IsNull(best);
        }

        [Test]
        public void HatChain_IgnoresBelowTargets_ButCanAttachAboveALooseStack()
        {
            var targets = new List<SnapTarget> { Below(new Vector2(100, 100)), Above(new Vector2(100, 160)) };

            var best = SnapResolver.FindBest(targets, Chain(new Vector2(100, 100), height: 58f, hat: true));

            Assert.AreEqual(SnapKind.AboveStack, best.Value.Kind);
        }

        [Test]
        public void CapChain_CannotGoWhereBlocksAlreadyFollow_OrAboveAStack()
        {
            var targets = new List<SnapTarget> { Below(new Vector2(100, 100), hasFollowers: true), Above(new Vector2(100, 130)) };

            Assert.IsNull(SnapResolver.FindBest(targets, Chain(new Vector2(100, 100), cap: true)));
        }

        [Test]
        public void CapChain_CanGoAtTheEndOfASequence()
        {
            var targets = new List<SnapTarget> { Below(new Vector2(100, 100), hasFollowers: false) };

            Assert.IsNotNull(SnapResolver.FindBest(targets, Chain(new Vector2(100, 100), cap: true)));
        }
    }
}
