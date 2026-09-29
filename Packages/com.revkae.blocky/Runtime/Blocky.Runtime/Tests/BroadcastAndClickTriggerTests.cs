using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>
    /// The two hats added for Scratch/Delightex parity: <c>when I receive</c>, driven by the <c>broadcast</c>
    /// statement, and <c>when clicked</c>, driven by the broker's click raycast.
    /// </summary>
    public class BroadcastAndClickTriggerTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("BroadcastTestTarget");
            _target.SetActive(false); // OnEnable must not fire before BlockyRuntime.SetForTests wires the fixtures
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            BlockyRuntime.Reset();
        }

        private static BlockDefinition Def(string blockType, string executorKey, BlockShape shape, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = executorKey;
            def.shape = shape;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static readonly ParamSpec[] MessageParam = { new() { key = "message", kind = ParamKind.Text } };

        private static readonly ParamSpec[] MoveParams =
        {
            new() { key = "distance", kind = ParamKind.Number, min = -1000, max = 1000 },
            new() { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
        };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", "unused", BlockShape.Trigger),
            Def("event.when_clicked", "unused", BlockShape.Trigger),
            Def("event.when_broadcast_received", "unused", BlockShape.Trigger, MessageParam),
            Def("event.broadcast", "event.broadcast", BlockShape.Statement, MessageParam),
            Def("motion.move_forward", "motion.move_forward", BlockShape.Statement, MoveParams)
        });

        private static BlockNode Move() => new()
        {
            id = "n_move",
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = 1f },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = 0f }
            }
        };

        private static BlockStack Listener(string message) => new()
        {
            id = "stk_listener",
            triggerBlockType = "event.when_broadcast_received",
            triggerParameters = new[] { new BlockParam { key = "message", kind = ParamKind.Text, text = message } },
            sequence = new[] { Move() }
        };

        private static BlockStack Sender(string message) => new()
        {
            id = "stk_sender",
            triggerBlockType = "event.when_play_clicked",
            sequence = new[]
            {
                new BlockNode
                {
                    id = "n_broadcast",
                    blockType = "event.broadcast",
                    parameters = new[] { new BlockParam { key = "message", kind = ParamKind.Text, text = message } }
                }
            }
        };

        private (TriggerBroker broker, VmScheduler scheduler) Run(params BlockStack[] stacks)
        {
            var registry = BuildRegistry();
            var broker = new TriggerBroker();
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            BlockyRuntime.SetForTests(registry, scheduler, broker);

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(new ObjectProgram { targetObjectUid = "obj_1", stacks = stacks });

            var runner = _target.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            runner.Initialize(); // the deterministic seam: edit-mode OnEnable timing is not guaranteed in the test runner

            return (broker, scheduler);
        }

        private float Z => _target.transform.position.z;

        [Test]
        public void Broadcast_StartsTheScriptListeningForThatMessage()
        {
            var (broker, scheduler) = Run(Sender("jump"), Listener("jump"));

            broker.FirePlayClicked();
            scheduler.Tick(1f); // the sender broadcasts; the listener is queued
            scheduler.Tick(1f); // and runs on the next tick, never inside the sender's own tick

            Assert.AreEqual(1f, Z, 1e-4f);
        }

        [Test]
        public void Broadcast_LeavesAScriptWaitingForADifferentMessageAlone()
        {
            var (broker, scheduler) = Run(Sender("jump"), Listener("duck"));

            broker.FirePlayClicked();
            scheduler.Tick(1f);
            scheduler.Tick(1f);

            Assert.AreEqual(0f, Z, 1e-4f);
        }

        [Test]
        public void BroadcastNames_MatchIgnoringCaseAndSurroundingSpace()
        {
            var (broker, scheduler) = Run(Listener("Jump"));

            broker.Broadcast("  jump ");
            scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f);
        }

        [Test]
        public void WhenClicked_StartsTheScript_OnlyForTheObjectTheRayHit()
        {
            var other = new GameObject("SomethingElse");
            try
            {
                var (broker, scheduler) = Run(new BlockStack
                {
                    id = "stk_clicked",
                    triggerBlockType = "event.when_clicked",
                    sequence = new[] { Move() }
                });

                broker.RaiseClicked(other);
                scheduler.Tick(1f);
                Assert.AreEqual(0f, Z, 1e-4f, "a click on another object is not a click on this one");

                broker.RaiseClicked(_target);
                scheduler.Tick(1f);

                Assert.AreEqual(1f, Z, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void WhenClicked_AlsoFires_WhenTheRayHitsAChildOfTheProgrammedObject()
        {
            var child = new GameObject("ChildCollider");
            child.transform.SetParent(_target.transform);

            var (broker, scheduler) = Run(new BlockStack
            {
                id = "stk_clicked",
                triggerBlockType = "event.when_clicked",
                sequence = new[] { Move() }
            });

            broker.RaiseClicked(child);
            scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f);
        }
    }
}
