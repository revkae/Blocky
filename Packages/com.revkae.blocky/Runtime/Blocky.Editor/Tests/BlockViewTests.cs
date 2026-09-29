using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor.Tests
{
    public class BlockViewTests
    {
        private static BlockDefinition Def(string blockType, BlockShape shape, BlockCategory category, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.shape = shape;
            def.category = category;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static BlockRegistry BuildRegistry()
        {
            return BlockRegistry.Build(new[]
            {
                Def("event.when_play_clicked", BlockShape.Trigger, BlockCategory.Event),
                Def("motion.move_forward", BlockShape.Statement, BlockCategory.Motion,
                    parameters: new[]
                    {
                        new ParamSpec { key = "distance", kind = ParamKind.Number },
                        new ParamSpec { key = "duration", kind = ParamKind.Number }
                    }),
                Def("control.repeat", BlockShape.CBlock, BlockCategory.Control, branchCount: 1,
                    parameters: new[] { new ParamSpec { key = "times", kind = ParamKind.Number } })
            });
        }

        private static BlockNode Move(string id, float distance, float duration) => new()
        {
            id = id,
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = distance },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = duration }
            }
        };

        [Test]
        public void Create_StatementBlock_HasCategoryAndShapeClasses_AndReadOnlyFieldsWithValues()
        {
            var registry = BuildRegistry();
            var view = BlockView.Create(Move("n1", 3f, 1.5f), registry);

            Assert.IsInstanceOf<BlockView>(view);
            Assert.IsTrue(view.ClassListContains("blocky-block"));
            Assert.IsTrue(view.ClassListContains("blocky-block--category-motion"));
            Assert.IsTrue(view.ClassListContains("blocky-block--shape-statement"));

            var floatFields = view.Query<FloatField>().ToList();
            Assert.AreEqual(2, floatFields.Count);
            Assert.AreEqual(3f, floatFields[0].value);
            Assert.AreEqual(1.5f, floatFields[1].value);
            Assert.IsFalse(floatFields[0].enabledSelf);
        }

        [Test]
        public void Create_CBlockWithNestedChild_RendersOneBodySlotWithNestedBlockView()
        {
            var registry = BuildRegistry();
            var repeatNode = new BlockNode
            {
                id = "n1",
                blockType = "control.repeat",
                parameters = new[] { new BlockParam { key = "times", kind = ParamKind.Number, number = 4 } },
                branches = new[] { new[] { Move("n2", 1f, 0f) } }
            };

            var view = (BlockView)BlockView.Create(repeatNode, registry);

            var bodySlots = view.Query(className: "blocky-block__body-slot").ToList();
            Assert.AreEqual(1, bodySlots.Count);

            var nested = bodySlots[0].Query<BlockView>().ToList();
            Assert.AreEqual(1, nested.Count);
            Assert.AreEqual("n2", nested[0].NodeId);
        }

        [Test]
        public void Create_UnknownBlockType_RendersErrorPlaceholder_PreservingParams()
        {
            var registry = BuildRegistry();
            var node = new BlockNode
            {
                id = "n1",
                blockType = "does.not_exist",
                parameters = new[] { new BlockParam { key = "foo", kind = ParamKind.Number, number = 7 } }
            };

            var view = BlockView.Create(node, registry);

            Assert.IsInstanceOf<UnknownBlockView>(view);
            Assert.IsTrue(view.ClassListContains("blocky-block--error"));
            Assert.AreEqual("n1", ((UnknownBlockView)view).NodeId);

            var labels = view.Query<Label>().ToList();
            Assert.IsTrue(labels.Exists(l => l.text.Contains("does.not_exist")));
            Assert.IsTrue(labels.Exists(l => l.text.Contains("foo") && l.text.Contains("7")));
        }

        [Test]
        public void StackView_RendersTriggerHeaderAndSequence()
        {
            var registry = BuildRegistry();
            var stack = new BlockStack
            {
                id = "stk_1",
                triggerBlockType = "event.when_play_clicked",
                sequence = new[] { Move("n1", 2f, 0f) }
            };

            var view = new StackView(stack, registry);

            Assert.AreEqual("stk_1", view.StackId);
            Assert.IsTrue(view.Query(className: "blocky-block--shape-trigger").ToList().Count == 1);

            var nested = view.Query<BlockView>().ToList();
            Assert.AreEqual(1, nested.Count);
            Assert.AreEqual("n1", nested[0].NodeId);
        }
    }
}
