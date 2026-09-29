using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    public class ConditionEvaluationTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("BlockyConditionTarget");

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            BlockyInput.SetForTests(null);
            BlockyInput.IsPointerOverUi = null;
        }

        private static BlockDefinition Def(string blockType, BlockShape shape, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static readonly ParamSpec[] ConditionSpec = { new() { key = "condition", kind = ParamKind.Reporter } };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("motion.move_forward", BlockShape.Statement, parameters: new[]
            {
                new ParamSpec { key = "distance", kind = ParamKind.Number, min = -1000, max = 1000 },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
            }),
            Def("control.if", BlockShape.CBlock, 1, ConditionSpec),
            Def("control.repeat_until", BlockShape.CBlock, 1, ConditionSpec),
            Def("condition.true", BlockShape.Boolean),
            Def("condition.false", BlockShape.Boolean),
            Def("condition.mouse_down", BlockShape.Boolean),
            Def("condition.mouse_up", BlockShape.Boolean)
        });

        private static BlockParam Slot(string conditionType) => new()
        {
            key = "condition",
            kind = ParamKind.Reporter,
            reporter = conditionType == null ? null : new BlockNode { id = "c_" + conditionType, blockType = conditionType }
        };

        private static BlockNode Move(string id) => new()
        {
            id = id,
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = 1f },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = 0f }
            }
        };

        private static BlockNode CBlock(string blockType, string conditionType) => new()
        {
            id = "n_" + blockType,
            blockType = blockType,
            parameters = new[] { Slot(conditionType) },
            branches = new[] { new[] { Move("n_body") } }
        };

        private (VmScheduler scheduler, VmThread thread) Start(BlockNode node)
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = new[] { node } } }
            };
            var result = ProgramCompiler.Link(program, registry);
            Assert.IsFalse(result.HasErrors);

            var assembly = typeof(OpTableBuilder).Assembly;
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, assembly), OpTableBuilder.BuildConditions(registry, assembly));
            return (scheduler, scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[0]));
        }

        private float Z => _target.transform.position.z;

        [TestCase("condition.true", 1f)]
        [TestCase("condition.false", 0f)]
        [TestCase(null, 0f)] // an empty slot reads as false
        public void If_RunsItsBody_OnlyWhenTheConditionInItsSlotIsTrue(string conditionType, float expectedZ)
        {
            var (scheduler, _) = Start(CBlock("control.if", conditionType));
            scheduler.Tick(1f);
            Assert.AreEqual(expectedZ, Z, 1e-4f);
        }

        [Test]
        public void RepeatUntilMouseDown_KeepsLooping_UntilTheButtonIsPressed()
        {
            var held = false;
            BlockyInput.SetForTests(() => held);
            var (scheduler, thread) = Start(CBlock("control.repeat_until", "condition.mouse_down"));

            scheduler.Tick(1f);
            scheduler.Tick(1f);
            Assert.AreEqual(2f, Z, 1e-4f);

            held = true; // re-evaluated on the next lap — not frozen at compile time
            scheduler.Tick(1f);

            Assert.AreEqual(2f, Z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void MouseDown_IgnoresPressesOverTheEditor()
        {
            BlockyInput.SetForTests(() => true);
            BlockyInput.IsPointerOverUi = () => true;
            var (scheduler, _) = Start(CBlock("control.if", "condition.mouse_down"));

            scheduler.Tick(1f);

            Assert.AreEqual(0f, Z, 1e-4f);
        }

        [Test]
        public void MouseUp_IsTrueWhileTheButtonIsNotHeld()
        {
            BlockyInput.SetForTests(() => false);
            var (scheduler, _) = Start(CBlock("control.if", "condition.mouse_up"));

            scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f);
        }

        [Test]
        public void OpTables_BindConditionsSeparatelyFromSteps()
        {
            var registry = BuildRegistry();
            var assembly = typeof(OpTableBuilder).Assembly;
            registry.TryGetOpcode("condition.true", out var trueOpcode);

            Assert.IsNull(OpTableBuilder.Build(registry, assembly)[trueOpcode]);
            Assert.IsInstanceOf<Ops.TrueCondition>(OpTableBuilder.BuildConditions(registry, assembly)[trueOpcode]);
        }
    }
}
