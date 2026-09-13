using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Editor.Tests
{
    public class StackPlacementResolverTests
    {
        [Test]
        public void ReturnsDesiredPosition_WhenNoCollision()
        {
            var result = StackPlacementResolver.FindFreePosition(new Vector2(100, 100), new List<Vector2>(), new Vector2(50, 50));
            Assert.AreEqual(new Vector2(100, 100), result);
        }

        [Test]
        public void NudgesAwayFromCollidingPosition_UntilFree()
        {
            var existing = new List<Vector2> { new(100, 100) };
            var result = StackPlacementResolver.FindFreePosition(new Vector2(100, 100), existing, new Vector2(50, 50));

            Assert.AreNotEqual(new Vector2(100, 100), result);
            Assert.IsFalse(new Rect(result, new Vector2(50, 50)).Overlaps(new Rect(100, 100, 50, 50)));
        }
    }
}
