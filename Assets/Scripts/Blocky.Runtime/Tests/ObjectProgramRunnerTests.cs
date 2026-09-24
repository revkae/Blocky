using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    public class ObjectProgramRunnerTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("RunnerTestTarget");
            _target.SetActive(false); // OnEnable must not fire before dependencies are wired via BlockyRuntime.SetForTests
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            BlockyRuntime.Reset();
        }

        private static BlockDefinition Def(string blockType, string executorKey, BlockShape shape,
            RetriggerPolicy retrigger = RetriggerPolicy.RestartOnRetrigger, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = executorKey;
            def.shape = shape;
            def.retrigger = retrigger;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static readonly ParamSpec[] MoveParams =
        {
            new() { key = "distance", kind = ParamKind.Number, min = -1000, max = 1000 },
            new() { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
        };

        private static BlockRegistry BuildPlayClickedRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", "unused", BlockShape.Trigger),
            Def("motion.move_forward", "motion.move_forward", BlockShape.Statement, parameters: MoveParams)
        });

        private static BlockRegistry BuildCollisionRegistry(RetriggerPolicy retrigger) => BlockRegistry.Build(new[]
        {
            Def("event.when_collided", "unused", BlockShape.Trigger, retrigger),
            Def("motion.move_forward", "motion.move_forward", BlockShape.Statement, parameters: MoveParams)
        });

        private static ObjectProgram BuildProgram(string triggerBlockType, float distance, float duration) => new()
        {
            targetObjectUid = "obj_1",
            stacks = new[]
            {
                new BlockStack
                {
                    id = "stk_1",
                    triggerBlockType = triggerBlockType,
                    sequence = new[]
                    {
                        new BlockNode
                        {
                            id = "n1",
                            blockType = "motion.move_forward",
                            parameters = new[]
                            {
                                new BlockParam { key = "distance", kind = ParamKind.Number, number = distance },
                                new BlockParam { key = "duration", kind = ParamKind.Number, number = duration }
                            }
                        }
                    }
                }
            }
        };

        private ObjectProgramRunner AddRunner(BlockRegistry registry, ObjectProgram program, TriggerBroker broker, VmScheduler scheduler)
        {
            BlockyRuntime.SetForTests(registry, scheduler, broker);

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(program);

            var runner = _target.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            // Call the seam directly rather than SetActive(true): edit-mode OnEnable timing via GameObject
            // activation is not guaranteed synchronous inside the test runner.
            runner.Initialize();

            return runner;
        }

        [Test]
        public void AnEmptyScript_DoesNotStopTheScriptAfterIt()
        {
            // A hat left on the table with nothing under it has no code, so its script began where the next one
            // does — and a thread started for either ended at the empty one's end: the real script never ran.
            var registry = BuildPlayClickedRegistry();
            var broker = new TriggerBroker();
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            var program = BuildProgram("event.when_play_clicked", 1f, 0f);
            program.stacks = new[]
            {
                new BlockStack { id = "stk_empty", triggerBlockType = "event.when_play_clicked" },
                program.stacks[0]
            };
            AddRunner(registry, program, broker, scheduler);

            broker.FirePlayClicked();
            scheduler.Tick(1f);

            Assert.AreEqual(1f, _target.transform.position.z, 1e-4f);
        }

        [Test]
        public void PlayClickedTrigger_StartsThread_AndMovesTarget()
        {
            var registry = BuildPlayClickedRegistry();
            var broker = new TriggerBroker();
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            AddRunner(registry, BuildProgram("event.when_play_clicked", 1f, 0f), broker, scheduler);

            broker.FirePlayClicked();
            scheduler.Tick(1f);

            Assert.AreEqual(1f, _target.transform.position.z, 1e-4f);
        }

        [Test]
        public void Disable_HaltsThread_SoFurtherTicksDoNotMoveIt()
        {
            var registry = BuildPlayClickedRegistry();
            var broker = new TriggerBroker();
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            var runner = AddRunner(registry, BuildProgram("event.when_play_clicked", 1f, 0f), broker, scheduler);

            broker.FirePlayClicked();
            runner.Shutdown(); // halts the thread and unsubscribes

            scheduler.Tick(1f);

            Assert.AreEqual(0f, _target.transform.position.z, 1e-4f);
        }

        [Test]
        public void Disable_Unsubscribes_SoLaterTriggersDoNothing()
        {
            var registry = BuildPlayClickedRegistry();
            var broker = new TriggerBroker();
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            var runner = AddRunner(registry, BuildProgram("event.when_play_clicked", 1f, 0f), broker, scheduler);

            runner.Shutdown();
            broker.FirePlayClicked(); // fired after shutdown — no listener left to react
            scheduler.Tick(1f);

            Assert.AreEqual(0f, _target.transform.position.z, 1e-4f);
        }

        [Test]
        public void IgnoreWhileRunning_SecondCollisionWhileBusy_DoesNothing()
        {
            var registry = BuildCollisionRegistry(RetriggerPolicy.IgnoreWhileRunning);
            var broker = new TriggerBroker();
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            AddRunner(registry, BuildProgram("event.when_collided", 1f, 10f), broker, scheduler);

            broker.RaiseCollided(_target, null);
            broker.RaiseCollided(_target, null); // ignored — first thread is still running

            scheduler.Tick(1f);

            Assert.AreEqual(0.1f, _target.transform.position.z, 1e-3f); // only one thread's progress (distance 1 over duration 10, one 1s tick)
        }

        [Test]
        public void RestartOnRetrigger_SecondCollisionWhileBusy_StartsAFreshThread()
        {
            var registry = BuildCollisionRegistry(RetriggerPolicy.RestartOnRetrigger);
            var broker = new TriggerBroker();
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            AddRunner(registry, BuildProgram("event.when_collided", 1f, 10f), broker, scheduler);

            broker.RaiseCollided(_target, null);
            scheduler.Tick(1f); // first thread's only tick before being restarted: +0.1
            broker.RaiseCollided(_target, null); // restarts — old thread killed, new one starts fresh
            scheduler.Tick(1f); // fresh thread's first tick: another +0.1 from zero

            Assert.AreEqual(0.2f, _target.transform.position.z, 1e-3f);
        }
    }
}
