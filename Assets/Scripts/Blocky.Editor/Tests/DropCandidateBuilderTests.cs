using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor.Tests
{
    public class DropCandidateBuilderTests
    {
        private static BlockDefinition Def(string blockType, BlockShape shape, int branchCount = 0)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.shape = shape;
            def.branchCount = branchCount;
            return def;
        }

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("motion.move_forward", BlockShape.Statement),
            Def("control.repeat", BlockShape.CBlock, branchCount: 1)
        });

        [Test]
        public void EmptySequence_ProducesOneSequenceEndCandidate()
        {
            var registry = BuildRegistry();
            var stack = new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked" };
            var view = new StackView(stack, registry);

            var rects = new Dictionary<VisualElement, Rect> { [view.SequenceContainer] = new Rect(0, 0, 200, 40) };
            var candidates = DropCandidateBuilder.Build(view, el => rects.TryGetValue(el, out var r) ? r : default);

            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual(DropCandidateKind.SequenceEnd, candidates[0].Kind);
            Assert.AreEqual(0, candidates[0].Index);
            Assert.IsNull(candidates[0].ParentNodeId);
        }

        [Test]
        public void TwoTopLevelBlocks_ProduceGapAndEndCandidatesWithCorrectIndices()
        {
            var registry = BuildRegistry();
            var stack = new BlockStack
            {
                id = "stk_1",
                triggerBlockType = "event.when_play_clicked",
                sequence = new[]
                {
                    new BlockNode { id = "n1", blockType = "motion.move_forward" },
                    new BlockNode { id = "n2", blockType = "motion.move_forward" }
                }
            };
            var view = new StackView(stack, registry);
            var blockViews = view.Query<BlockView>().ToList();

            var rects = new Dictionary<VisualElement, Rect>
            {
                [blockViews[0]] = new Rect(0, 0, 200, 30),
                [blockViews[1]] = new Rect(0, 30, 200, 30)
            };
            var candidates = DropCandidateBuilder.Build(view, el => rects.TryGetValue(el, out var r) ? r : default);

            Assert.AreEqual(3, candidates.Count); // gap-before-n1, gap-before-n2, end
            Assert.AreEqual(DropCandidateKind.SequenceGap, candidates[0].Kind);
            Assert.AreEqual(0, candidates[0].Index);
            Assert.AreEqual(DropCandidateKind.SequenceGap, candidates[1].Kind);
            Assert.AreEqual(1, candidates[1].Index);
            Assert.AreEqual(DropCandidateKind.SequenceEnd, candidates[2].Kind);
            Assert.AreEqual(2, candidates[2].Index);
        }

        [Test]
        public void EmptyBodySlot_ProducesOneBodyCavityCandidate_AtDepthOne()
        {
            var registry = BuildRegistry();
            var stack = new BlockStack
            {
                id = "stk_1",
                triggerBlockType = "event.when_play_clicked",
                sequence = new[] { new BlockNode { id = "n1", blockType = "control.repeat", branches = new[] { new BlockNode[0] } } }
            };
            var view = new StackView(stack, registry);
            var repeatView = view.Query<BlockView>().ToList()[0];

            var rects = new Dictionary<VisualElement, Rect>
            {
                [repeatView] = new Rect(0, 0, 200, 30),
                [repeatView.BodySlots[0]] = new Rect(10, 30, 180, 20)
            };
            var candidates = DropCandidateBuilder.Build(view, el => rects.TryGetValue(el, out var r) ? r : default);

            var cavityIndex = candidates.FindIndex(c => c.Kind == DropCandidateKind.BodyCavity);
            Assert.GreaterOrEqual(cavityIndex, 0);
            var cavity = candidates[cavityIndex];
            Assert.AreEqual("n1", cavity.ParentNodeId);
            Assert.AreEqual(0, cavity.BranchIndex);
            Assert.AreEqual(1, cavity.Depth);
        }
    }
}
