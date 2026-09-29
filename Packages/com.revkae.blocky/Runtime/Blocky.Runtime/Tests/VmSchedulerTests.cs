using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    public class VmSchedulerTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("BlockyTestTarget");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
        }

        private static BlockDefinition Def(string blockType, string executorKey, BlockShape shape, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = executorKey;
            def.shape = shape;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static BlockRegistry BuildRegistry()
        {
            return BlockRegistry.Build(new[]
            {
                Def("event.when_play_clicked", "unused", BlockShape.Trigger),
                Def("motion.move_forward", "motion.move_forward", BlockShape.Statement,
                    parameters: new[]
                    {
                        new ParamSpec { key = "distance", kind = ParamKind.Number, min = -1000, max = 1000 },
                        new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
                    }),
                Def("motion.set_position", "motion.set_position", BlockShape.Statement,
                    parameters: new[]
                    {
                        new ParamSpec { key = "x", kind = ParamKind.Number, min = -1000, max = 1000 },
                        new ParamSpec { key = "y", kind = ParamKind.Number, min = -1000, max = 1000 },
                        new ParamSpec { key = "z", kind = ParamKind.Number, min = -1000, max = 1000 },
                        new ParamSpec
                        {
                            key = "space", kind = ParamKind.Choice,
                            choices = new[] { new ChoiceEntry { stableId = "world" }, new ChoiceEntry { stableId = "local" } }
                        }
                    }),
                Def("control.wait", "control.wait", BlockShape.Statement,
                    parameters: new[] { new ParamSpec { key = "seconds", kind = ParamKind.Number, min = 0, max = 1000 } }),
                Def("control.repeat", "control.repeat", BlockShape.CBlock, branchCount: 1,
                    parameters: new[] { new ParamSpec { key = "times", kind = ParamKind.Number, min = 0, max = 100000 } }),
                Def("control.repeat_forever", "control.repeat_forever", BlockShape.CBlock, branchCount: 1),
                Def("control.repeat_until", "control.repeat_until", BlockShape.CBlock, branchCount: 1,
                    parameters: new[] { new ParamSpec { key = "condition", kind = ParamKind.Bool } }),
                Def("control.if", "control.if", BlockShape.CBlock, branchCount: 1,
                    parameters: new[] { new ParamSpec { key = "condition", kind = ParamKind.Bool } }),
                Def("control.if_else", "control.if_else", BlockShape.CBlock, branchCount: 2,
                    parameters: new[] { new ParamSpec { key = "condition", kind = ParamKind.Bool } })
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

        private static BlockNode Wait(string id, float seconds) => new()
        {
            id = id,
            blockType = "control.wait",
            parameters = new[] { new BlockParam { key = "seconds", kind = ParamKind.Number, number = seconds } }
        };

        private static BlockNode Repeat(string id, float times, BlockNode[] body) => new()
        {
            id = id,
            blockType = "control.repeat",
            parameters = new[] { new BlockParam { key = "times", kind = ParamKind.Number, number = times } },
            branches = new[] { body }
        };

        private static BlockNode RepeatForever(string id, BlockNode[] body) => new()
        {
            id = id,
            blockType = "control.repeat_forever",
            branches = new[] { body }
        };

        private static BlockNode RepeatUntil(string id, bool condition, BlockNode[] body) => new()
        {
            id = id,
            blockType = "control.repeat_until",
            parameters = new[] { new BlockParam { key = "condition", kind = ParamKind.Bool, boolean = condition } },
            branches = new[] { body }
        };

        private static BlockNode If(string id, bool condition, BlockNode[] body) => new()
        {
            id = id,
            blockType = "control.if",
            parameters = new[] { new BlockParam { key = "condition", kind = ParamKind.Bool, boolean = condition } },
            branches = new[] { body }
        };

        private static BlockNode IfElse(string id, bool condition, BlockNode[] trueBody, BlockNode[] falseBody) => new()
        {
            id = id,
            blockType = "control.if_else",
            parameters = new[] { new BlockParam { key = "condition", kind = ParamKind.Bool, boolean = condition } },
            branches = new[] { trueBody, falseBody }
        };

        private static BlockNode SetPosition(string id, float x, float y, float z, string space) => new()
        {
            id = id,
            blockType = "motion.set_position",
            parameters = new[]
            {
                new BlockParam { key = "x", kind = ParamKind.Number, number = x },
                new BlockParam { key = "y", kind = ParamKind.Number, number = y },
                new BlockParam { key = "z", kind = ParamKind.Number, number = z },
                new BlockParam { key = "space", kind = ParamKind.Choice, text = space }
            }
        };

        private static ObjectProgram Program(params BlockNode[] sequence) => new()
        {
            targetObjectUid = "obj_1",
            stacks = new[]
            {
                new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = sequence }
            }
        };

        private (VmScheduler scheduler, CompiledProgram compiled) BuildScheduler(ObjectProgram program)
        {
            var registry = BuildRegistry();
            var result = ProgramCompiler.Link(program, registry);
            Assert.IsFalse(result.HasErrors);
            var opTable = OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly);
            return (new VmScheduler(opTable), result.Program);
        }

        [Test]
        public void MoveForwardInstant_RunsInOneTick_ThenDone()
        {
            var (scheduler, compiled) = BuildScheduler(Program(Move("n1", 1f, 0f)));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);

            Assert.AreEqual(1f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void MoveForwardOverTime_UsesRetryAcrossTicks()
        {
            var (scheduler, compiled) = BuildScheduler(Program(Move("n1", 10f, 2f)));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f); // halfway
            Assert.AreEqual(5f, _target.transform.position.z, 1e-3f);
            Assert.AreEqual(ThreadState.YieldedFrame, thread.State); // Retry yields until the next tick

            scheduler.Tick(1f); // finished
            Assert.AreEqual(10f, _target.transform.position.z, 1e-3f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void Wait_DelaysExecutionUntilWakeTime()
        {
            // Wait only evaluates part-way into the first tick (after dt has already been added to "now"),
            // so "wait 1s" wakes at now = 0.5 + 1.0 = 1.5, not at elapsed 1.0 from program start.
            var (scheduler, compiled) = BuildScheduler(Program(Wait("n1", 1f), Move("n2", 1f, 0f)));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(0.5f); // now = 0.5; wait starts, wakes at 1.5
            Assert.AreEqual(0f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(ThreadState.Sleeping, thread.State);

            scheduler.Tick(0.6f); // now = 1.1, still short of 1.5
            Assert.AreEqual(0f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(ThreadState.Sleeping, thread.State);

            scheduler.Tick(0.5f); // now = 1.6, past the wake time
            Assert.AreEqual(1f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void Repeat_ForcesOneLapPerTick_WhenBodyNeverYields()
        {
            var (scheduler, compiled) = BuildScheduler(Program(Repeat("n1", 5, new[] { Move("n2", 1f, 0f) })));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            for (var lap = 1; lap <= 5; lap++)
            {
                scheduler.Tick(1f);
                Assert.AreEqual(lap, _target.transform.position.z, 1e-4f, $"after tick {lap}");
                Assert.AreEqual(ThreadState.YieldedFrame, thread.State, $"after tick {lap}"); // forced yield at lap wrap
            }

            scheduler.Tick(1f); // one more tick to detect the exhausted counter and fall through
            Assert.AreEqual(5f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void Budget_CapsPerTickWork_AndFiresRunawayEvent_ForANonLoopingSequence()
        {
            var nodes = new BlockNode[20];
            for (var i = 0; i < nodes.Length; i++)
                nodes[i] = Move($"n{i}", 1f, 0f);

            var (scheduler, compiled) = BuildScheduler(Program(nodes));
            scheduler.InstructionBudget = 5;
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            var fired = false;
            scheduler.OnRunawayThread += _ => fired = true;

            scheduler.Tick(1f);

            Assert.IsTrue(fired);
            Assert.AreEqual(5f, _target.transform.position.z, 1e-4f); // only 5 of 20 moves ran this tick
            Assert.AreEqual(ThreadState.Running, thread.State); // plain Continue never changes state; only budget stopped it

            scheduler.Tick(1f); // 10
            scheduler.Tick(1f); // 15
            scheduler.Tick(1f); // 20 — pc reaches Code.Length exactly as budget runs out, not yet detected as Done
            scheduler.Tick(1f); // this tick's first Step call finds pc out of bounds and marks the thread Done

            Assert.AreEqual(20f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void SetPosition_World_IsInstant()
        {
            var (scheduler, compiled) = BuildScheduler(Program(SetPosition("n1", 1f, 2f, 3f, "world")));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);

            Assert.AreEqual(new Vector3(1f, 2f, 3f), _target.transform.position);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void If_TrueCondition_RunsBodyThenContinues()
        {
            var (scheduler, compiled) = BuildScheduler(Program(
                If("n1", true, new[] { Move("n2", 1f, 0f) }),
                Move("n3", 10f, 0f)));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);

            Assert.AreEqual(11f, _target.transform.position.z, 1e-4f); // both the branch move and the sibling ran
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void If_FalseCondition_SkipsBodyEntirely()
        {
            var (scheduler, compiled) = BuildScheduler(Program(
                If("n1", false, new[] { Move("n2", 1f, 0f) }),
                Move("n3", 10f, 0f)));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);

            Assert.AreEqual(10f, _target.transform.position.z, 1e-4f); // only the sibling ran
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void IfElse_TrueCondition_RunsTrueBranchOnly()
        {
            var (scheduler, compiled) = BuildScheduler(Program(
                IfElse("n1", true, new[] { Move("n2", 1f, 0f) }, new[] { Move("n3", 100f, 0f) }),
                Move("n4", 10f, 0f)));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);

            Assert.AreEqual(11f, _target.transform.position.z, 1e-4f); // true branch (1) + sibling (10), never the false branch (100)
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void IfElse_FalseCondition_RunsFalseBranchOnly()
        {
            var (scheduler, compiled) = BuildScheduler(Program(
                IfElse("n1", false, new[] { Move("n2", 100f, 0f) }, new[] { Move("n3", 1f, 0f) }),
                Move("n4", 10f, 0f)));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);

            Assert.AreEqual(11f, _target.transform.position.z, 1e-4f); // false branch (1) + sibling (10), never the true branch (100)
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void RepeatForever_NeverFinishes_ButYieldsEveryTick()
        {
            var (scheduler, compiled) = BuildScheduler(Program(RepeatForever("n1", new[] { Move("n2", 1f, 0f) })));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            for (var tick = 1; tick <= 4; tick++)
            {
                scheduler.Tick(1f);
                Assert.AreEqual(tick, _target.transform.position.z, 1e-4f, $"after tick {tick}");
                Assert.AreEqual(ThreadState.YieldedFrame, thread.State, $"after tick {tick}");
            }
        }

        [Test]
        public void RepeatUntil_TrueConditionUpFront_NeverRunsBody()
        {
            var (scheduler, compiled) = BuildScheduler(Program(RepeatUntil("n1", true, new[] { Move("n2", 1f, 0f) })));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);

            Assert.AreEqual(0f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void RepeatUntil_FalseCondition_LoopsForeverLikeRepeatForever()
        {
            // Condition is a Bool literal in v1 (no expression blocks yet), so "false" can never become true —
            // this is expected to behave exactly like repeat_forever until Reporter params land.
            var (scheduler, compiled) = BuildScheduler(Program(RepeatUntil("n1", false, new[] { Move("n2", 1f, 0f) })));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            for (var tick = 1; tick <= 3; tick++)
            {
                scheduler.Tick(1f);
                Assert.AreEqual(tick, _target.transform.position.z, 1e-4f, $"after tick {tick}");
                Assert.AreEqual(ThreadState.YieldedFrame, thread.State, $"after tick {tick}");
            }
        }
    }
}
