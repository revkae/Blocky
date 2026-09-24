using System;
using Blocky.Compiler;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>An op outside Blocky.Runtime, as a game's own block would be — this test assembly references Blocky.Runtime.</summary>
    [BlockExecutor("test.found_by_the_default_scan")]
    public sealed class FoundByTheDefaultScanTestOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx) => OpResult.Continue;
    }

    public class OpTableBuilderTests
    {
        private static BlockDefinition Def(string blockType, string executorKey, BlockShape shape = BlockShape.Statement)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = executorKey;
            def.shape = shape;
            return def;
        }

        [Test]
        public void Build_BindsKnownExecutorKeys()
        {
            var registry = BlockRegistry.Build(new[]
            {
                Def("motion.move_forward", "motion.move_forward"),
                Def("control.wait", "control.wait"),
                Def("control.repeat", "control.repeat")
            });

            var table = OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly);

            registry.TryGetOpcode("motion.move_forward", out var moveOpcode);
            Assert.IsInstanceOf<Ops.MoveForwardOp>(table[moveOpcode]);
        }

        [Test]
        public void Build_UnknownExecutorKey_Throws()
        {
            var registry = BlockRegistry.Build(new[] { Def("motion.teleport", "no.such.op") });
            Assert.Throws<InvalidOperationException>(() => OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
        }

        [Test]
        public void Build_SkipsTriggers()
        {
            var registry = BlockRegistry.Build(new[] { Def("event.when_play_clicked", "unbound.executor", BlockShape.Trigger) });
            Assert.DoesNotThrow(() => OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
        }

        [Test]
        public void AnOpInAnotherAssembly_IsFoundWithNoRegistration()
        {
            var registry = BlockRegistry.Build(new[] { Def("test.found_by_the_default_scan", "test.found_by_the_default_scan") });

            var table = OpTableBuilder.Build(registry); // no assemblies named: every one that references Blocky.Runtime

            Assert.IsInstanceOf<FoundByTheDefaultScanTestOp>(table[0]);
        }

        [Test]
        public void ForPlay_AMissingOp_IsReportedAndLeftEmpty_InsteadOfStoppingEveryBlock()
        {
            var registry = BlockRegistry.Build(new[]
            {
                Def("motion.move_forward", "motion.move_forward"),
                Def("game.not_written_yet", "game.not_written_yet")
            });
            var reported = new System.Collections.Generic.List<string>();

            var (steps, _, _) = OpTableBuilder.BuildAllForPlay(registry, reported.Add);

            registry.TryGetOpcode("motion.move_forward", out var move);
            registry.TryGetOpcode("game.not_written_yet", out var missing);
            Assert.IsNotNull(steps[move]);
            Assert.IsNull(steps[missing]);
            Assert.AreEqual(1, reported.Count);
            StringAssert.Contains("game.not_written_yet", reported[0]);
        }

        [Test]
        public void ForPlay_AScriptThatReachesAMissingOp_StopsThere_AndTheOthersRun()
        {
            var move = Def("motion.move_forward", "motion.move_forward");
            move.parameters = new[]
            {
                new ParamSpec { key = "distance", kind = Blocky.Data.ParamKind.Number, min = -10, max = 10 },
                new ParamSpec { key = "duration", kind = Blocky.Data.ParamKind.Number, min = 0, max = 10 }
            };
            var registry = BlockRegistry.Build(new[] { Def("event.when_play_clicked", "unused", BlockShape.Trigger), move, Def("game.not_written_yet", "game.not_written_yet") });
            var (steps, conditions, values) = OpTableBuilder.BuildAllForPlay(registry, _ => { });
            var scheduler = new VmScheduler(steps, conditions, values);

            Blocky.Data.BlockNode Node(string id, string type) => new()
            {
                id = id, blockType = type,
                parameters = type == "motion.move_forward"
                    ? new[] { new Blocky.Data.BlockParam { key = "distance", kind = Blocky.Data.ParamKind.Number, number = 1f }, new Blocky.Data.BlockParam { key = "duration", kind = Blocky.Data.ParamKind.Number } }
                    : new Blocky.Data.BlockParam[0]
            };
            var program = ProgramCompiler.Link(new Blocky.Data.ObjectProgram
            {
                stacks = new[]
                {
                    new Blocky.Data.BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = new[] { Node("n1", "game.not_written_yet"), Node("n2", "motion.move_forward") } },
                    new Blocky.Data.BlockStack { id = "stk_2", triggerBlockType = "event.when_play_clicked", sequence = new[] { Node("n3", "motion.move_forward") } }
                }
            }, registry).Program;

            var target = new GameObject("MissingOpTarget");
            try
            {
                var broken = scheduler.Start(program, target, program.StackEntryPoints[0]);
                scheduler.Start(program, target, program.StackEntryPoints[1]);
                scheduler.Tick(0.1f);

                Assert.AreEqual(ScriptEndReason.Failed, broken.EndReason);
                Assert.AreEqual(1f, target.transform.position.z, 1e-4f, "only the other script's move ran");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// The shipped catalog, not a fixture: every block asset in Resources/Blocks has an op of the right kind.
        /// A new asset whose op is missing, misspelled or the wrong kind fails here instead of at the first Play.
        /// </summary>
        [Test]
        public void EveryBlockInTheCatalog_IsBoundToAnOp()
        {
            var registry = BlockRegistry.LoadFromResources();
            Assert.Greater(registry.Count, 0, "no block assets found");

            Assert.DoesNotThrow(() => OpTableBuilder.BuildAll(registry));
        }
    }
}
