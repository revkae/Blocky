using NUnit.Framework;

namespace Blocky.Data.Tests
{
    public class ProgramStoreCommandTests
    {
        private static ProgramStore BuildStore()
        {
            var program = new ObjectProgram
            {
                targetObjectUid = "obj_1",
                stacks = new[]
                {
                    new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked" }
                }
            };
            return new ProgramStore(program) { UndoCapacity = 10 };
        }

        [Test]
        public void InsertNode_Then_Undo_RestoresEmptySequence()
        {
            var store = BuildStore();
            var node = new BlockNode { id = "n_1", blockType = "motion.move_forward" };

            store.Apply(new InsertNode(NodeLocation.InStack("stk_1", 0), node));
            Assert.AreEqual(1, store.Program.stacks[0].sequence.Length);

            store.Undo();
            Assert.AreEqual(0, store.Program.stacks[0].sequence.Length);
        }

        [Test]
        public void SetParam_ChangesTriggerParameter_AndUndoRestoresIt()
        {
            var store = BuildStore();
            store.Program.stacks[0].triggerParameters = new[]
            {
                new BlockParam { key = "key", kind = ParamKind.Choice, text = "Space" }
            };

            store.Apply(new SetParam(new ParamTarget("stk_1", null), "key",
                new BlockParam { key = "key", kind = ParamKind.Choice, text = "Enter" }));
            Assert.AreEqual("Enter", store.Program.stacks[0].triggerParameters[0].text);

            store.Undo();
            Assert.AreEqual("Space", store.Program.stacks[0].triggerParameters[0].text);
        }

        [Test]
        public void CreateStack_Then_DeleteStack_RoundTrips()
        {
            var store = BuildStore();
            var newStack = new BlockStack { id = "stk_2", triggerBlockType = "event.when_play_clicked" };

            store.Apply(new CreateStack(newStack));
            Assert.AreEqual(2, store.Program.stacks.Length);

            store.Apply(new DeleteStack("stk_2"));
            Assert.AreEqual(1, store.Program.stacks.Length);
        }

        [Test]
        public void OnChanged_FiresWithExpectedKind()
        {
            var store = BuildStore();
            StructureChange? seen = null;
            store.OnChanged += change => seen = change;

            store.Apply(new MoveStack("stk_1", new UnityEngine.Vector2(10, 10)));

            Assert.IsTrue(seen.HasValue);
            Assert.AreEqual(StructureChangeKind.StackMoved, seen.Value.Kind);
        }

        [Test]
        public void Redo_PutsBackWhatUndoTookAway()
        {
            var store = BuildStore();
            var node = new BlockNode { id = "n_1", blockType = "motion.move_forward" };
            store.Apply(new InsertNode(NodeLocation.InStack("stk_1", 0), node));

            store.Undo();
            Assert.IsFalse(store.CanUndo);
            Assert.IsTrue(store.CanRedo);

            store.Redo();
            Assert.AreEqual(1, store.Program.stacks[0].sequence.Length);
            Assert.AreEqual("n_1", store.Program.stacks[0].sequence[0].id);
            Assert.IsFalse(store.CanRedo, "nothing left to put back");
            Assert.IsTrue(store.CanUndo, "and it can be taken away again");
        }

        [Test]
        public void ARedoneEditCanBeUndoneAgain()
        {
            var store = BuildStore();
            store.Apply(new InsertNode(NodeLocation.InStack("stk_1", 0), new BlockNode { id = "n_1", blockType = "motion.move_forward" }));
            store.Undo();
            store.Redo();
            store.Undo();

            Assert.AreEqual(0, store.Program.stacks[0].sequence.Length);
        }

        /// <summary>History has branched: what was undone can no longer be reached from what's on the table.</summary>
        [Test]
        public void AFreshEdit_ClearsWhatCouldBeRedone()
        {
            var store = BuildStore();
            store.Apply(new InsertNode(NodeLocation.InStack("stk_1", 0), new BlockNode { id = "n_1", blockType = "motion.move_forward" }));
            store.Undo();

            store.Apply(new InsertNode(NodeLocation.InStack("stk_1", 0), new BlockNode { id = "n_2", blockType = "motion.turn" }));

            Assert.IsFalse(store.CanRedo);
            Assert.AreEqual("n_2", store.Program.stacks[0].sequence[0].id);
        }

        [Test]
        public void RedoWithNothingUndone_DoesNothing()
        {
            var store = BuildStore();

            Assert.IsFalse(store.CanRedo);
            Assert.DoesNotThrow(() => store.Redo());
            Assert.AreEqual(0, store.Program.stacks[0].sequence.Length);
        }

        [Test]
        public void IdGenerator_ProducesUniqueSixteenCharIds()
        {
            var a = IdGenerator.NewId();
            var b = IdGenerator.NewId();

            Assert.AreEqual(16, a.Length);
            Assert.AreNotEqual(a, b);
        }
    }
}
