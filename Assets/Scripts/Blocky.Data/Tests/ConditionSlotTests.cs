using NUnit.Framework;
using UnityEngine;

namespace Blocky.Data.Tests
{
    public class ConditionSlotTests
    {
        private static BlockNode Condition(string id) => new() { id = id, blockType = "condition.true" };

        private static BlockNode If(string id, BlockNode condition) => new()
        {
            id = id,
            blockType = "control.if",
            parameters = new[] { new BlockParam { key = "condition", kind = ParamKind.Reporter, reporter = condition } },
            branches = new[] { new BlockNode[0] }
        };

        private static ProgramStore BuildStore()
        {
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = new[] { If("if_empty", null), If("if_full", Condition("c_old")) } },
                    new BlockStack { id = "stk_loose", triggerBlockType = "", sequence = new[] { Condition("c_loose") }, canvasPosition = new Vector2(300, 300) }
                }
            };
            return new ProgramStore(program) { UndoCapacity = 10 };
        }

        private static BlockNode SlotOf(ProgramStore store, string ownerId) =>
            ProgramQuery.FindNode(store.Program, "stk_1", ownerId).parameters[0].reporter;

        private static ChainTarget Slot(string ownerId, Vector2 eject = default) => ChainTarget.IntoConditionSlot("stk_1", ownerId, "condition", eject);

        [Test]
        public void NewCondition_DroppedIntoAnEmptySlot_FillsIt()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNewNode(Condition("c_new"), Slot("if_empty")));

            Assert.AreEqual("c_new", SlotOf(store, "if_empty").id);
            Assert.AreEqual(2, store.Program.stacks.Length);
        }

        [Test]
        public void DroppingIntoAFilledSlot_Swaps_AndThePreviousConditionPopsOutOntoTheTable()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNewNode(Condition("c_new"), Slot("if_full", new Vector2(40, 50))));

            Assert.AreEqual("c_new", SlotOf(store, "if_full").id);
            Assert.AreEqual(3, store.Program.stacks.Length);
            var ejected = store.Program.stacks[2];
            Assert.IsTrue(ProgramQuery.IsLoose(ejected));
            Assert.AreEqual("c_old", ejected.sequence[0].id);
            Assert.AreEqual(new Vector2(40, 50), ejected.canvasPosition);
        }

        [Test]
        public void ConditionPulledOutOfItsSlot_AndDroppedFree_LiesLoose_AndTheSlotEmpties()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromConditionSlot("stk_1", "if_full", "condition", ChainTarget.Free(new Vector2(10, 20))));

            Assert.IsNull(SlotOf(store, "if_full"));
            var loose = store.Program.stacks[2];
            Assert.AreEqual("c_old", loose.sequence[0].id);
            Assert.AreEqual(new Vector2(10, 20), loose.canvasPosition);
        }

        [Test]
        public void LooseCondition_DroppedIntoASlot_LeavesTheTable()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromStack("stk_loose", Slot("if_empty")));

            Assert.IsNull(ProgramQuery.FindStack(store.Program, "stk_loose"));
            Assert.AreEqual("c_loose", SlotOf(store, "if_empty").id);
        }

        [Test]
        public void MovingTheOwnerBlock_CarriesItsCondition()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNodes(NodeLocation.InStack("stk_1", 1), ChainTarget.Free(Vector2.zero)));

            var moved = store.Program.stacks[2].sequence[0];
            Assert.AreEqual("if_full", moved.id);
            Assert.AreEqual("c_old", moved.parameters[0].reporter.id);
        }

        [Test]
        public void DeleteBlock_OnAConditionInASlot_EmptiesTheSlot_AndKeepsTheBlock()
        {
            var store = BuildStore();
            store.Apply(new DeleteBlock("stk_1", "c_old"));

            Assert.IsNull(SlotOf(store, "if_full"));
            Assert.AreEqual(2, ProgramQuery.FindStack(store.Program, "stk_1").sequence.Length);
        }

        [Test]
        public void Undo_AfterASwap_RestoresBothConditions()
        {
            var store = BuildStore();
            store.Apply(DropChain.FromNewNode(Condition("c_new"), Slot("if_full")));
            store.Undo();

            Assert.AreEqual(2, store.Program.stacks.Length);
            Assert.AreEqual("c_old", SlotOf(store, "if_full").id);
        }
    }
}
