using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>Clones: the store that makes and counts them, and the three blocks that drive it.</summary>
    public class CloneTests
    {
        private GameObject _original;

        [SetUp]
        public void SetUp() => _original = new GameObject("Spawner");

        [TearDown]
        public void TearDown()
        {
            foreach (var clone in Object.FindObjectsByType<BlockyClone>(FindObjectsSortMode.None))
                if (clone != null) Object.DestroyImmediate(clone.gameObject);
            if (_original != null) Object.DestroyImmediate(_original);
            BlockyRuntime.Reset();
        }

        // ---- the store -------------------------------------------------------------------------------

        [Test]
        public void ACloneIsACopyOfTheOriginal_MarkedAsAClone()
        {
            var clones = new BlockyClones();
            _original.transform.position = new Vector3(1f, 2f, 3f);

            var clone = clones.Create(_original);

            Assert.IsNotNull(clone);
            Assert.AreNotSame(_original, clone);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), clone.transform.position, "it appears where the original stands");
            Assert.IsTrue(BlockyClones.IsClone(clone));
            Assert.IsFalse(BlockyClones.IsClone(_original), "and the original is still not a clone");
            Assert.AreEqual(1, clones.Count);
        }

        [Test]
        public void CloningLeavesTheOriginalActive()
        {
            var clones = new BlockyClones();

            clones.Create(_original);

            Assert.IsTrue(_original.activeSelf, "the original is only deactivated for the instant of the copy");
        }

        [Test]
        public void DeleteThisClone_RemovesAClone_ButNeverTheOriginal()
        {
            var clones = new BlockyClones();
            var clone = clones.Create(_original);

            Assert.IsFalse(clones.Delete(_original), "deleting the original is never what was meant");
            Assert.IsTrue(clones.Delete(clone));

            Assert.AreEqual(0, clones.Count);
            Assert.IsTrue(_original != null);
        }

        [Test]
        public void TheCloneCap_StopsARunawayLoop()
        {
            var clones = new BlockyClones();

            for (var i = 0; i < BlockyClones.MaxClones + 10; i++) clones.Create(_original);

            Assert.AreEqual(BlockyClones.MaxClones, clones.Count);
            Assert.IsNull(clones.Create(_original), "past the cap it quietly does nothing");
        }

        // ---- the blocks ------------------------------------------------------------------------------

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

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_go_clicked", "unused", BlockShape.Trigger),
            Def("event.when_i_start_as_a_clone", "unused", BlockShape.Trigger, RetriggerPolicy.AllowConcurrent),
            Def("control.create_clone", "control.create_clone", BlockShape.Statement,
                parameters: new[] { new ParamSpec { key = "object", kind = ParamKind.ObjectRef } }),
            Def("control.delete_this_clone", "control.delete_this_clone", BlockShape.Cap),
            Def("motion.move_forward", "motion.move_forward", BlockShape.Statement, parameters: MoveParams)
        });

        private static BlockNode Move(float distance) => new()
        {
            id = "n_move" + distance,
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = distance },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = 0f }
            }
        };

        /// <summary>A spawner that clones itself on Go, and a clone script that moves whoever runs it.</summary>
        private static ObjectProgram BuildProgram() => new()
        {
            targetObjectUid = "Spawner",
            stacks = new[]
            {
                new BlockStack
                {
                    id = "stk_spawn",
                    triggerBlockType = "event.when_go_clicked",
                    sequence = new[]
                    {
                        new BlockNode
                        {
                            id = "n_clone",
                            blockType = "control.create_clone",
                            parameters = new[] { new BlockParam { key = "object", kind = ParamKind.ObjectRef, text = "me" } }
                        }
                    }
                },
                new BlockStack
                {
                    id = "stk_clone",
                    triggerBlockType = "event.when_i_start_as_a_clone",
                    sequence = new[] { Move(5f) }
                }
            }
        };

        private (TriggerBroker broker, VmScheduler scheduler) Wire()
        {
            var registry = BuildRegistry();
            var broker = new TriggerBroker();
            var assembly = typeof(OpTableBuilder).Assembly;
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            BlockyRuntime.SetForTests(registry, scheduler, broker);

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(BuildProgram());

            _original.SetActive(false);
            var runner = _original.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            _original.SetActive(true);
            runner.Initialize();

            return (broker, scheduler);
        }

        [Test]
        public void CreateClone_MakesACopy_ThatRunsItsOwnCloneScript()
        {
            var (broker, scheduler) = Wire();

            broker.FireGoClicked();
            scheduler.Tick(1f); // the spawner clones itself; the clone's hat fires and queues its thread
            scheduler.Tick(1f); // which runs here

            var clones = Object.FindObjectsByType<BlockyClone>(FindObjectsSortMode.None);
            Assert.AreEqual(1, clones.Length);
            Assert.AreEqual(5f, clones[0].transform.position.z, 1e-3f, "the clone ran the clone script");
            Assert.AreEqual(0f, _original.transform.position.z, 1e-3f, "and the original did not");
        }

        [Test]
        public void CloningYourself_DoesNotStopYourOwnScript()
        {
            // The regression behind the rule in BlockyClones.Create: deactivating the original around the copy runs
            // OnDisable, which halts its threads — so the block after `create clone` never ran, and a loop that
            // cloned made exactly one copy however many laps it was given.
            var registry = BuildRegistry();
            var broker = new TriggerBroker();
            var assembly = typeof(OpTableBuilder).Assembly;
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            BlockyRuntime.SetForTests(registry, scheduler, broker);

            var program = new ObjectProgram
            {
                targetObjectUid = "Spawner",
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk_spawn",
                        triggerBlockType = "event.when_go_clicked",
                        sequence = new[]
                        {
                            new BlockNode
                            {
                                id = "n_clone",
                                blockType = "control.create_clone",
                                parameters = new[] { new BlockParam { key = "object", kind = ParamKind.ObjectRef, text = "me" } }
                            },
                            Move(3f)
                        }
                    }
                }
            };

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(program);
            _original.SetActive(false);
            var runner = _original.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            _original.SetActive(true);
            runner.Initialize();

            broker.FireGoClicked();
            scheduler.Tick(1f);

            Assert.AreEqual(3f, _original.transform.position.z, 1e-3f, "the block after `create clone` still ran");
        }

        [Test]
        public void ACloneIsNotStartedByTheHatThatMadeIt()
        {
            var (broker, scheduler) = Wire();

            broker.FireGoClicked();
            scheduler.Tick(1f);
            scheduler.Tick(1f);
            scheduler.Tick(1f);

            // The clone carries the spawner's "when Go clicked" script too, but Go already fired before it existed,
            // so it must not have cloned itself in turn.
            Assert.AreEqual(1, Object.FindObjectsByType<BlockyClone>(FindObjectsSortMode.None).Length);
        }

        [Test]
        public void Stop_DeletesEveryClone()
        {
            var (broker, scheduler) = Wire();
            broker.FireGoClicked();
            scheduler.Tick(1f);
            Assert.AreEqual(1, Object.FindObjectsByType<BlockyClone>(FindObjectsSortMode.None).Length);

            BlockyRuntime.Playback.Stop();

            Assert.AreEqual(0, BlockyRuntime.Clones.Count);
            Assert.AreEqual(0, Object.FindObjectsByType<BlockyClone>(FindObjectsSortMode.None).Length);
        }
    }
}
