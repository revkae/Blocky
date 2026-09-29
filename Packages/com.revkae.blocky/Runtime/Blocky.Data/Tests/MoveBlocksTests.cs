using System;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Data.Tests
{
    /// <summary>Moving and deleting several selected blocks at once: one command, one Undo.</summary>
    public class MoveBlocksTests
    {
        private const string Hat = "event.when_play_clicked";

        private static BlockNode Node(string id, params BlockNode[][] branches) => new()
        {
            id = id,
            blockType = "test.block",
            parameters = new BlockParam[0],
            branches = branches
        };

        private static BlockStack Stack(string id, string trigger, Vector2 at, params BlockNode[] sequence) => new()
        {
            id = id,
            triggerBlockType = trigger,
            triggerParameters = new BlockParam[0],
            canvasPosition = at,
            sequence = sequence
        };

        private static ProgramStore Store(params BlockStack[] stacks) => new(new ObjectProgram { stacks = stacks }) { UndoCapacity = 10 };

        private static BlockStack StackOf(ProgramStore store, string id) => ProgramQuery.FindStack(store.Program, id);

        [Test]
        public void By_MovesTwoLooseStacksByTheSameAmount_AndOneUndoPutsBothBack()
        {
            var store = Store(Stack("s1", "", new Vector2(0, 0), Node("n1")), Stack("s2", "", new Vector2(100, 0), Node("n2")));
            var blocks = new[] { new BlockRef("s1", "n1"), new BlockRef("s2", "n2") };

            store.Apply(MoveBlocks.By(blocks, new[] { Vector2.zero, new Vector2(100, 0) }, new Vector2(10, 20)));

            Assert.AreEqual(new Vector2(10, 20), StackOf(store, "s1").canvasPosition);
            Assert.AreEqual(new Vector2(110, 20), StackOf(store, "s2").canvasPosition);

            store.Undo();
            Assert.AreEqual(new Vector2(0, 0), StackOf(store, "s1").canvasPosition);
            Assert.AreEqual(new Vector2(100, 0), StackOf(store, "s2").canvasPosition);
        }

        [Test]
        public void By_ABlockUnderASelectedHat_MovesOnlyWithItsStack()
        {
            var store = Store(Stack("s1", Hat, new Vector2(0, 0), Node("n1"), Node("n2")));
            var blocks = new[] { new BlockRef("s1", null), new BlockRef("s1", "n2") };

            Assert.AreEqual(1, MoveBlocks.Outermost(store.Program, blocks).Count);

            store.Apply(MoveBlocks.By(blocks, new[] { Vector2.zero, new Vector2(0, 40) }, new Vector2(5, 5)));

            Assert.AreEqual(1, store.Program.stacks.Length);
            Assert.AreEqual(2, StackOf(store, "s1").sequence.Length);
            Assert.AreEqual(new Vector2(5, 5), StackOf(store, "s1").canvasPosition);
        }

        [Test]
        public void By_ABlockMidStack_LeavesWithTheBlocksBelowIt_AndLandsWhereItWasPlusTheMove()
        {
            var store = Store(
                Stack("s1", Hat, new Vector2(0, 0), Node("n1"), Node("n2"), Node("n3")),
                Stack("s2", "", new Vector2(200, 0), Node("n4")));
            var blocks = new[] { new BlockRef("s1", "n2"), new BlockRef("s2", "n4") };

            store.Apply(MoveBlocks.By(blocks, new[] { new Vector2(0, 40), new Vector2(200, 0) }, new Vector2(5, 5)));

            Assert.AreEqual(3, store.Program.stacks.Length);
            Assert.AreEqual(1, StackOf(store, "s1").sequence.Length);
            Assert.AreEqual(new Vector2(205, 5), StackOf(store, "s2").canvasPosition);

            var moved = Array.Find(store.Program.stacks, s => s.sequence.Length > 0 && s.sequence[0].id == "n2");
            Assert.IsNotNull(moved);
            Assert.IsTrue(ProgramQuery.IsLoose(moved));
            Assert.AreEqual(2, moved.sequence.Length); // n3 came along
            Assert.AreEqual(new Vector2(5, 45), moved.canvasPosition);
        }

        [Test]
        public void Discard_DeletesEveryBlockWithWhatItCarries_AndUndoBringsThemBack()
        {
            var store = Store(Stack("s1", Hat, new Vector2(0, 0), Node("n1")), Stack("s2", "", new Vector2(100, 0), Node("n2")));

            store.Apply(MoveBlocks.Discard(new[] { new BlockRef("s1", null), new BlockRef("s2", "n2") }));
            Assert.AreEqual(0, store.Program.stacks.Length);

            store.Undo();
            Assert.AreEqual(2, store.Program.stacks.Length);
        }

        [Test]
        public void DeleteBlocks_DeletesEach_AndSkipsOnesThatWentWithAnEarlierOne()
        {
            var store = Store(Stack("s1", Hat, new Vector2(0, 0), Node("c", new[] { Node("inner") }), Node("n2")));

            store.Apply(new DeleteBlocks(new[] { new BlockRef("s1", "c"), new BlockRef("s1", "inner"), new BlockRef("s1", "n2") }));

            Assert.AreEqual(0, StackOf(store, "s1").sequence.Length);
            Assert.IsFalse(ProgramQuery.IsLoose(StackOf(store, "s1"))); // the hat wasn't selected, so it stays

            store.Undo();
            Assert.AreEqual(2, StackOf(store, "s1").sequence.Length);
        }
    }
}
