using System;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Data.Tests
{
    public class DropChainTests
    {
        private static BlockNode Node(string id) => new() { id = id, blockType = "motion.move_forward" };

        private static ProgramStore BuildStore()
        {
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack { id = "stk_hat", triggerBlockType = "event.when_play_clicked", sequence = new[] { Node("a"), Node("b"), Node("c") } },
                    new BlockStack { id = "stk_loose", triggerBlockType = "", sequence = new[] { Node("x"), Node("y") }, canvasPosition = new Vector2(300, 300) }
                }
            };
            return new ProgramStore(program) { UndoCapacity = 10 };
        }

        private static BlockStack Stack(ProgramStore store, string id) => ProgramQuery.FindStack(store.Program, id);

        private static string[] Ids(BlockNode[] nodes) => Array.ConvertAll(nodes, n => n.id);

        [Test]
        public void NewNode_DroppedFree_BecomesALooseStackAtThatPosition()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNewNode(Node("n"), ChainTarget.Free(new Vector2(50, 60))));

            Assert.AreEqual(3, store.Program.stacks.Length);
            var created = store.Program.stacks[2];
            Assert.IsTrue(ProgramQuery.IsLoose(created));
            CollectionAssert.AreEqual(new[] { "n" }, Ids(created.sequence));
            Assert.AreEqual(new Vector2(50, 60), created.canvasPosition);
        }

        [Test]
        public void PickingUpAMiddleNode_TakesEverythingBelowIt()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNodes(NodeLocation.InStack("stk_hat", 1), ChainTarget.Free(Vector2.zero)));

            CollectionAssert.AreEqual(new[] { "a" }, Ids(Stack(store, "stk_hat").sequence));
            CollectionAssert.AreEqual(new[] { "b", "c" }, Ids(store.Program.stacks[2].sequence));
        }

        [Test]
        public void WholeLooseStackInsertedIntoAnother_IsMerged_AndTheEmptyLooseStackIsRemoved()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNodes(NodeLocation.InStack("stk_loose", 0), ChainTarget.Insert(NodeLocation.InStack("stk_hat", 1))));

            Assert.IsNull(Stack(store, "stk_loose"));
            CollectionAssert.AreEqual(new[] { "a", "x", "y", "b", "c" }, Ids(Stack(store, "stk_hat").sequence));
        }

        [Test]
        public void NewTrigger_AttachedAboveLooseStack_TurnsItIntoARunnableStack()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNewTrigger("event.when_go_clicked", new BlockParam[0],
                ChainTarget.AttachAbove("stk_loose", new Vector2(300, 250))));

            var stack = Stack(store, "stk_loose");
            Assert.AreEqual("event.when_go_clicked", stack.triggerBlockType);
            CollectionAssert.AreEqual(new[] { "x", "y" }, Ids(stack.sequence));
            Assert.AreEqual(new Vector2(300, 250), stack.canvasPosition);
        }

        [Test]
        public void WholeStackMovedFree_KeepsItsIdAndTrigger()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromStack("stk_hat", ChainTarget.Free(new Vector2(10, 20))));

            var stack = Stack(store, "stk_hat");
            Assert.AreEqual("event.when_play_clicked", stack.triggerBlockType);
            Assert.AreEqual(new Vector2(10, 20), stack.canvasPosition);
            Assert.AreEqual(3, stack.sequence.Length);
        }

        [Test]
        public void Discard_RemovesTheChain_ButATriggeredStackSurvivesBeingEmptied()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNodes(NodeLocation.InStack("stk_hat", 0), ChainTarget.Discard()));

            Assert.IsNotNull(Stack(store, "stk_hat"));
            Assert.AreEqual(0, Stack(store, "stk_hat").sequence.Length);
        }

        [Test]
        public void HatChain_CannotBeInsertedIntoASequence_AndLeavesTheProgramUntouched()
        {
            var store = BuildStore();

            Assert.Throws<InvalidOperationException>(() => store.Apply(DropChain.FromNewTrigger("event.when_go_clicked",
                new BlockParam[0], ChainTarget.Insert(NodeLocation.InStack("stk_hat", 0)))));
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Ids(Stack(store, "stk_hat").sequence));
        }

        [Test]
        public void Undo_RestoresTheProgramExactly()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNodes(NodeLocation.InStack("stk_loose", 0), ChainTarget.Insert(NodeLocation.InStack("stk_hat", 3))));
            store.Undo();

            Assert.AreEqual(2, store.Program.stacks.Length);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Ids(Stack(store, "stk_hat").sequence));
            CollectionAssert.AreEqual(new[] { "x", "y" }, Ids(Stack(store, "stk_loose").sequence));
        }
    }
}
