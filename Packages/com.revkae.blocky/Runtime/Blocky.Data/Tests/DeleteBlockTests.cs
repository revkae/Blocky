using System;
using NUnit.Framework;

namespace Blocky.Data.Tests
{
    public class DeleteBlockTests
    {
        private static BlockNode Node(string id) => new() { id = id, blockType = "motion.move_forward" };

        private static ProgramStore BuildStore() => new(new ObjectProgram
        {
            stacks = new[]
            {
                new BlockStack { id = "stk_hat", triggerBlockType = "event.when_play_clicked", sequence = new[] { Node("a"), Node("b"), Node("c") } },
                new BlockStack { id = "stk_loose", triggerBlockType = "", sequence = new[] { Node("x") } },
                new BlockStack { id = "stk_lonely_hat", triggerBlockType = "event.when_go_clicked" }
            }
        }) { UndoCapacity = 10 };

        private static BlockStack Stack(ProgramStore store, string id) => ProgramQuery.FindStack(store.Program, id);

        private static string[] Ids(BlockNode[] nodes) => Array.ConvertAll(nodes, n => n.id);

        [Test]
        public void DeletingAMiddleBlock_ClosesTheGap()
        {
            var store = BuildStore();
            store.Apply(new DeleteBlock("stk_hat", "b"));

            CollectionAssert.AreEqual(new[] { "a", "c" }, Ids(Stack(store, "stk_hat").sequence));
        }

        [Test]
        public void DeletingTheLastBlockOfALooseStack_RemovesTheStack()
        {
            var store = BuildStore();
            store.Apply(new DeleteBlock("stk_loose", "x"));

            Assert.IsNull(Stack(store, "stk_loose"));
        }

        [Test]
        public void DeletingAHat_KeepsItsBlocksAsALooseStack()
        {
            var store = BuildStore();
            store.Apply(new DeleteBlock("stk_hat", null));

            var stack = Stack(store, "stk_hat");
            Assert.IsTrue(ProgramQuery.IsLoose(stack));
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Ids(stack.sequence));
        }

        [Test]
        public void DeletingAHatWithNothingUnderIt_RemovesTheStack()
        {
            var store = BuildStore();
            store.Apply(new DeleteBlock("stk_lonely_hat", null));

            Assert.IsNull(Stack(store, "stk_lonely_hat"));
        }

        [Test]
        public void Undo_RestoresTheDeletedBlock()
        {
            var store = BuildStore();
            store.Apply(new DeleteBlock("stk_hat", "b"));
            store.Undo();

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Ids(Stack(store, "stk_hat").sequence));
        }
    }
}
