using System.Collections.Generic;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Compiler.Tests
{
    public class ProgramAdviceTests
    {
        private static BlockDefinition Def(string blockType, string displayName, BlockShape shape, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.displayNameKey = displayName;
            def.shape = shape;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_go_clicked", "when Go clicked", BlockShape.Trigger),
            Def("motion.move_forward", "Move Forward", BlockShape.Statement, parameters: new[]
            {
                new ParamSpec { key = "distance", kind = ParamKind.Number, min = -100, max = 100 }
            }),
            Def("control.repeat", "Repeat", BlockShape.CBlock, 1, new[] { new ParamSpec { key = "times", kind = ParamKind.Number, min = 0, max = 1000 } }),
            Def("control.if", "If", BlockShape.CBlock, 1, new[] { new ParamSpec { key = "condition", kind = ParamKind.Reporter } }),
            Def("condition.true", "true", BlockShape.Boolean)
        });

        private static BlockNode Move(string id, float distance = 1f) => new()
        {
            id = id,
            blockType = "motion.move_forward",
            parameters = new[] { new BlockParam { key = "distance", kind = ParamKind.Number, number = distance } }
        };

        private static BlockNode Repeat(string id, params BlockNode[] body) => new()
        {
            id = id,
            blockType = "control.repeat",
            parameters = new[] { new BlockParam { key = "times", kind = ParamKind.Number, number = 4 } },
            branches = new[] { body }
        };

        private static BlockNode If(string id, BlockNode condition, params BlockNode[] body) => new()
        {
            id = id,
            blockType = "control.if",
            parameters = new[] { new BlockParam { key = "condition", kind = ParamKind.Reporter, reporter = condition } },
            branches = new[] { body }
        };

        private static BlockNode True(string id) => new() { id = id, blockType = "condition.true" };

        private static BlockStack Script(string id, params BlockNode[] sequence) =>
            new() { id = id, triggerBlockType = "event.when_go_clicked", sequence = sequence };

        private static BlockStack Loose(string id, params BlockNode[] sequence) =>
            new() { id = id, triggerBlockType = "", sequence = sequence };

        private static List<Advice> Collect(params BlockStack[] stacks) =>
            ProgramAdvice.Collect(new ObjectProgram { stacks = stacks }, BuildRegistry());

        [Test]
        public void AWorkingScript_GetsNoAdvice()
        {
            var advice = Collect(Script("stk_1", Repeat("n_repeat", Move("n_move")), If("n_if", True("n_true"), Move("n_move2"))));

            CollectionAssert.IsEmpty(advice);
        }

        [Test]
        public void LooseBlocks_HintOnTheFirstBlock_NamesTheGoEvent()
        {
            var advice = Collect(Loose("stk_loose", Move("n1"), Move("n2")));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual(AdviceKind.Hint, advice[0].Kind);
            Assert.AreEqual("n1", advice[0].NodeId);
            StringAssert.Contains("never run", advice[0].Message);
            StringAssert.Contains("when Go clicked", advice[0].Message);
        }

        [Test]
        public void ALooseCondition_IsToldWhereItGoes()
        {
            var advice = Collect(Loose("stk_loose", True("n_true")));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual("n_true", advice[0].NodeId);
            StringAssert.Contains("⬡ hole", advice[0].Message);
            StringAssert.Contains("“If”", advice[0].Message);
        }

        [Test]
        public void AnEventWithNothingUnderIt_HintsOnTheHat()
        {
            var advice = Collect(Script("stk_1"));

            Assert.AreEqual(1, advice.Count);
            Assert.IsNull(advice[0].NodeId);
            Assert.AreEqual("stk_1", advice[0].StackId);
            StringAssert.Contains("nothing happens", advice[0].Message);
        }

        [Test]
        public void AnEmptyCBlock_Hints()
        {
            var advice = Collect(Script("stk_1", Repeat("n_repeat")));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual("n_repeat", advice[0].NodeId);
            StringAssert.Contains("“Repeat” has nothing inside", advice[0].Message);
        }

        [Test]
        public void AnEmptyConditionHole_ExplainsItCountsAsFalse()
        {
            var advice = Collect(Script("stk_1", If("n_if", null, Move("n_move"))));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual(AdviceKind.Hint, advice[0].Kind);
            Assert.AreEqual("n_if", advice[0].NodeId);
            StringAssert.Contains("counts as “false”", advice[0].Message);
        }

        [Test]
        public void ANumberOutOfRange_IsAProblem_ThatGivesTheRange()
        {
            var advice = Collect(Script("stk_1", Move("n_move", 500f)));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual(AdviceKind.Problem, advice[0].Kind);
            StringAssert.Contains("from -100 to 100", advice[0].Message);
            StringAssert.Contains("won't run", advice[0].Message);
        }

        [Test]
        public void AnUnknownBlock_IsAProblem()
        {
            var advice = Collect(Script("stk_1", new BlockNode { id = "n_gone", blockType = "looks.removed_block" }));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual(AdviceKind.Problem, advice[0].Kind);
            Assert.AreEqual("n_gone", advice[0].NodeId);
        }

        [Test]
        public void ACompilerErrorNoRuleCovers_StillGetsAMessage()
        {
            // A condition dropped into a runnable sequence is a compile error with no dedicated rule.
            var advice = Collect(Script("stk_1", True("n_true")));

            var problem = advice.Find(a => a.Kind == AdviceKind.Problem);
            Assert.AreEqual("n_true", problem.NodeId);
            StringAssert.Contains("won't run", problem.Message);
        }
    }
}
