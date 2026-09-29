using System.Collections.Generic;
using NUnit.Framework;

namespace Blocky.Data.Tests
{
    /// <summary><see cref="ProgramStore.BlockLimit"/>: the store refuses edits past a level's block limit, whatever made them.</summary>
    public class ProgramStoreLimitTests
    {
        private static BlockNode Block(string id) => new() { id = id, blockType = "motion.move_forward" };

        private static ProgramStore Store(int limit, params BlockNode[] sequence)
        {
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_go_clicked", sequence = sequence } }
            };
            return new ProgramStore(program) { UndoCapacity = 10, BlockLimit = limit };
        }

        private static InsertNode AddAtEnd(ProgramStore store, string id) =>
            new(NodeLocation.InStack("stk_1", store.Program.stacks[0].sequence.Length), Block(id));

        [Test]
        public void UnderTheLimit_AnEditIsAppliedAndHeardAsUsual()
        {
            var store = Store(2, Block("n1"));
            var heard = new List<StructureChange>();
            store.OnChanged += heard.Add;

            Assert.IsTrue(store.Apply(AddAtEnd(store, "n2")));

            Assert.AreEqual(2, store.Program.stacks[0].sequence.Length);
            Assert.AreEqual(1, heard.Count);
            Assert.IsTrue(store.CanUndo);
        }

        [Test]
        public void AnEditPastTheLimit_IsRefused_WithoutATrace()
        {
            var store = Store(2, Block("n1"), Block("n2"));
            var heard = new List<StructureChange>();
            var refused = 0;
            store.OnChanged += heard.Add;
            store.LimitRefused += () => refused++;

            Assert.IsFalse(store.Apply(AddAtEnd(store, "n3")));

            Assert.AreEqual(2, store.Program.stacks[0].sequence.Length, "the program is exactly as it was");
            CollectionAssert.IsEmpty(heard, "listeners never saw the block come and go");
            Assert.AreEqual(1, refused);
            Assert.IsFalse(store.CanUndo, "nothing to undo: nothing happened");
        }

        [Test]
        public void AHatDoesNotCount()
        {
            var store = Store(1, Block("n1"));

            Assert.IsTrue(store.Apply(new CreateStack(new BlockStack { id = "stk_2", triggerBlockType = "event.when_go_clicked" })));

            Assert.AreEqual(2, store.Program.stacks.Length);
        }

        [Test]
        public void OverTheLimitAlready_TakingAwayIsAllowed_AddingIsNot()
        {
            // A program from before the limit (a save, the scene's) may already have more than it allows.
            var store = Store(1, Block("n1"), Block("n2"), Block("n3"));

            Assert.IsFalse(store.Apply(AddAtEnd(store, "n4")));
            Assert.IsTrue(store.Apply(new DeleteBlock("stk_1", "n3")));

            Assert.AreEqual(2, store.Program.stacks[0].sequence.Length);
        }

        [Test]
        public void NoLimit_MeansNoRefusals()
        {
            var store = Store(0, Block("n1"), Block("n2"));

            Assert.IsTrue(store.Apply(AddAtEnd(store, "n3")));

            Assert.AreEqual(3, store.Program.stacks[0].sequence.Length);
        }
    }
}
