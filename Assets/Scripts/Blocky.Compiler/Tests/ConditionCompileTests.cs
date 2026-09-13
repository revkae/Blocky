using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Compiler.Tests
{
    public class ConditionCompileTests
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
            Def("motion.move_forward", BlockShape.Statement),
            Def(ProgramUpgrades.TrueConditionType, BlockShape.Boolean)
        });

        private static BlockParam Slot(BlockNode condition) => new() { key = "condition", kind = ParamKind.Reporter, reporter = condition };

        private static BlockNode If(BlockParam condition) => new()
        {
            id = "n_if",
            blockType = "control.if",
            parameters = new[] { condition },
            branches = new[] { new BlockNode[0] }
        };

        private static ObjectProgram Program(bool loose, params BlockNode[] sequence) => new()
        {
            stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = loose ? "" : "event.when_play_clicked", sequence = sequence } }
        };

        private static ParamValue FirstParamOfFirstInstruction(CompileResult result) =>
            result.Program.ParamTable[result.Program.Code[0].ParamOffset];

        [Test]
        public void FilledSlot_CompilesToAReporterPointingAtTheCondition()
        {
            var registry = BuildRegistry();
            var result = ProgramCompiler.Link(Program(false, If(Slot(new BlockNode { id = "c1", blockType = "condition.true" }))), registry);

            Assert.IsFalse(result.HasErrors);
            registry.TryGetOpcode("condition.true", out var conditionOpcode);
            var value = FirstParamOfFirstInstruction(result);
            Assert.AreEqual(ParamKind.Reporter, value.Kind);
            Assert.AreEqual(conditionOpcode, value.ReporterOpcode);
        }

        [Test]
        public void EmptySlot_IsValid_AndCompilesToAnEmptyReporter()
        {
            var result = ProgramCompiler.Link(Program(false, If(Slot(null))), BuildRegistry());

            Assert.IsFalse(result.HasErrors);
            Assert.AreEqual(-1, FirstParamOfFirstInstruction(result).ReporterOpcode);
        }

        [Test]
        public void OldCheckboxCondition_StillCompilesAsItsValue()
        {
            var result = ProgramCompiler.Link(Program(false, If(new BlockParam { key = "condition", kind = ParamKind.Bool, boolean = true })), BuildRegistry());

            Assert.IsFalse(result.HasErrors);
            var value = FirstParamOfFirstInstruction(result);
            Assert.AreEqual(ParamKind.Bool, value.Kind);
            Assert.IsTrue(value.Boolean);
        }

        [Test]
        public void ConditionInARunnableSequence_IsAnError()
        {
            var result = ProgramCompiler.Link(Program(false, new BlockNode { id = "c1", blockType = "condition.true" }), BuildRegistry());
            Assert.IsTrue(result.HasErrors);
        }

        [Test]
        public void ConditionLyingLooseOnTheTable_IsFine()
        {
            var result = ProgramCompiler.Link(Program(true, new BlockNode { id = "c1", blockType = "condition.true" }), BuildRegistry());
            Assert.IsFalse(result.HasErrors);
        }

        [Test]
        public void StatementInAConditionSlot_IsAnError()
        {
            var result = ProgramCompiler.Link(Program(false, If(Slot(new BlockNode { id = "m1", blockType = "motion.move_forward" }))), BuildRegistry());
            Assert.IsTrue(result.HasErrors);
        }

        [Test]
        public void Upgrade_TurnsATickedCheckboxIntoATrueBlock_AndAnUntickedOneIntoAnEmptySlot()
        {
            var registry = BuildRegistry();
            var ticked = If(new BlockParam { key = "condition", kind = ParamKind.Bool, boolean = true });
            var unticked = If(new BlockParam { key = "condition", kind = ParamKind.Bool, boolean = false });
            unticked.id = "n_if2";
            var program = Program(false, ticked, unticked);

            Assert.IsTrue(ProgramUpgrades.UpgradeCheckboxConditions(program, registry));

            Assert.AreEqual(ParamKind.Reporter, ticked.parameters[0].kind);
            Assert.AreEqual("condition.true", ticked.parameters[0].reporter.blockType);
            Assert.AreEqual(ParamKind.Reporter, unticked.parameters[0].kind);
            Assert.IsNull(unticked.parameters[0].reporter);
            Assert.IsFalse(ProgramUpgrades.UpgradeCheckboxConditions(program, registry), "a second pass has nothing left to upgrade");
            Assert.IsFalse(ProgramCompiler.Link(program, registry).HasErrors);
        }
    }
}
