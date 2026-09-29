using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor.Tests
{
    public class ConditionViewTests
    {
        private static BlockDefinition Def(string blockType, BlockShape shape, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.shape = shape;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("control.if", BlockShape.CBlock, 1, new[] { new ParamSpec { key = "condition", kind = ParamKind.Reporter } }),
            Def("condition.true", BlockShape.Boolean)
        });

        private static BlockNode If(string id, BlockNode condition) => new()
        {
            id = id,
            blockType = "control.if",
            parameters = new[] { new BlockParam { key = "condition", kind = ParamKind.Reporter, reporter = condition } },
            branches = new[] { new BlockNode[0] }
        };

        private static ProgramCanvasView BuildCanvas()
        {
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk_1",
                        triggerBlockType = "event.when_play_clicked",
                        sequence = new[] { If("if_full", new BlockNode { id = "c1", blockType = "condition.true" }), If("if_empty", null) }
                    },
                    new BlockStack { id = "stk_loose", triggerBlockType = "", sequence = new[] { new BlockNode { id = "c_loose", blockType = "condition.true" } } }
                }
            };
            return new ProgramCanvasView(new ProgramStore(program), BuildRegistry(), CanvasMode.Table);
        }

        [Test]
        public void FilledSlot_HoldsTheConditionView_WhichKnowsItsSlot()
        {
            var canvas = BuildCanvas();

            var slot = canvas.Query<ConditionSlot>().Where(s => s.OwnerNodeId == "if_full").First();
            Assert.IsFalse(slot.IsEmpty);
            var condition = slot.Q<ConditionView>();
            Assert.AreEqual("c1", condition.NodeId);
            Assert.AreEqual("if_full", condition.OwnerNodeId);
            Assert.AreEqual("condition", condition.ParamKey);
        }

        [Test]
        public void EmptySlot_IsEmpty()
        {
            var slot = BuildCanvas().Query<ConditionSlot>().Where(s => s.OwnerNodeId == "if_empty").First();
            Assert.IsTrue(slot.IsEmpty);
        }

        [Test]
        public void LooseCondition_LiesOnTheTable_OutsideAnySlot()
        {
            var condition = BuildCanvas().StackViews["stk_loose"].Q<ConditionView>();
            Assert.IsNotNull(condition);
            Assert.IsFalse(condition.IsInSlot);
        }

        [Test]
        public void SelectingACondition_HighlightsIt()
        {
            var canvas = BuildCanvas();
            canvas.Select("stk_1", "c1");

            var condition = canvas.Query<ConditionView>().Where(c => c.NodeId == "c1").First();
            Assert.IsTrue(condition.ClassListContains(BlockOutline.SelectedClass));
        }

        [Test]
        public void SlotResolver_PicksTheNearestSlotInReach_AndTheInnermostOnATie()
        {
            var outer = new ConditionSlotTarget("stk_1", "outer", "condition", new Rect(0, 0, 200, 40));
            var inner = new ConditionSlotTarget("stk_1", "inner", "condition", new Rect(50, 5, 60, 30));
            var far = new ConditionSlotTarget("stk_1", "far", "condition", new Rect(500, 500, 44, 24));
            var slots = new[] { outer, inner, far };

            Assert.AreEqual("inner", ConditionSlotResolver.FindBest(slots, new Vector2(60, 20))?.OwnerNodeId);
            Assert.AreEqual("far", ConditionSlotResolver.FindBest(slots, new Vector2(490, 510))?.OwnerNodeId);
            Assert.IsNull(ConditionSlotResolver.FindBest(slots, new Vector2(1000, 1000)));
        }
    }
}
