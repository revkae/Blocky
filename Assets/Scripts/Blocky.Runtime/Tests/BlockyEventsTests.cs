using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Blocky.Runtime.Tests
{
    /// <summary>A block that always throws — what a buggy custom op looks like to the scheduler.</summary>
    [BlockExecutor("test.always_fails")]
    public sealed class AlwaysFailsTestOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx) => throw new InvalidOperationException("this block always fails");
    }

    /// <summary><see cref="BlockyEvents"/>: what game code hears, and the level-complete blocks.</summary>
    public class BlockyEventsTests
    {
        private readonly List<GameObject> _objects = new();
        private readonly List<string> _heard = new();
        private VmScheduler _scheduler;
        private TriggerBroker _broker;

        private void OnStarted(BlockyScript s) => _heard.Add($"started {s.Object.name} #{s.Index} {s.Trigger}");
        private void OnFinished(BlockyScript s, ScriptEndReason reason) => _heard.Add($"finished {s.Object.name} #{s.Index} {reason}");
        private void OnAllFinished() => _heard.Add("all finished");
        private void OnLevelCompleted(GameObject by) => _heard.Add($"level complete by {(by != null ? by.name : "code")}");
        private void OnMessage(string message) => _heard.Add($"message {message}");
        private void OnStopped() => _heard.Add("stopped");
        private void OnWorldReset() => _heard.Add("reset");

        [SetUp]
        public void SetUp()
        {
            _heard.Clear(); // NUnit runs every test on one instance of the fixture
            BlockyEvents.ScriptStarted += OnStarted;
            BlockyEvents.ScriptFinished += OnFinished;
            BlockyEvents.AllScriptsFinished += OnAllFinished;
            BlockyEvents.LevelCompleted += OnLevelCompleted;
            BlockyEvents.MessageSent += OnMessage;
            BlockyEvents.Stopped += OnStopped;
            BlockyEvents.WorldReset += OnWorldReset;

            var registry = BuildRegistry();
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, typeof(OpTableBuilder).Assembly, typeof(BlockyEventsTests).Assembly);
            _scheduler = new VmScheduler(steps, conditions, values);
            _broker = new TriggerBroker();
            BlockyRuntime.SetForTests(registry, _scheduler, _broker);
        }

        [TearDown]
        public void TearDown()
        {
            BlockyEvents.ScriptStarted -= OnStarted;
            BlockyEvents.ScriptFinished -= OnFinished;
            BlockyEvents.AllScriptsFinished -= OnAllFinished;
            BlockyEvents.LevelCompleted -= OnLevelCompleted;
            BlockyEvents.MessageSent -= OnMessage;
            BlockyEvents.Stopped -= OnStopped;
            BlockyEvents.WorldReset -= OnWorldReset;

            foreach (var go in _objects) Object.DestroyImmediate(go);
            _objects.Clear();
            BlockyRuntime.Reset();
        }

        private static BlockDefinition Def(string blockType, BlockShape shape, int branchCount = 0, params ParamSpec[] parameters)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.branchCount = branchCount;
            def.parameters = parameters;
            return def;
        }

        private static ParamSpec Num(string key) => new() { key = key, kind = ParamKind.Number, min = -1000, max = 1000 };
        private static ParamSpec Str(string key) => new() { key = key, kind = ParamKind.Text };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("event.when_go_clicked", BlockShape.Trigger),
            Def("event.when_broadcast_received", BlockShape.Trigger, 0, Str("message")),
            Def("event.when_level_complete", BlockShape.Trigger),
            Def("event.broadcast", BlockShape.Statement, 0, Str("message")),
            Def("event.level_complete", BlockShape.Statement),
            Def("motion.move_forward", BlockShape.Statement, 0, Num("distance"), Num("duration")),
            Def("control.repeat_forever", BlockShape.CBlock, 1),
            Def("control.stop", BlockShape.Statement, 0, new ParamSpec
            {
                key = "target", kind = ParamKind.Choice, defaultText = "this_script",
                choices = new[] { new ChoiceEntry { stableId = "this_script" }, new ChoiceEntry { stableId = "all" }, new ChoiceEntry { stableId = "other_scripts" } }
            }),
            Def("test.always_fails", BlockShape.Statement)
        });

        private static int _nextId;

        private static BlockNode Node(string blockType, params BlockParam[] parameters) =>
            new() { id = "e" + ++_nextId, blockType = blockType, parameters = parameters };

        private static BlockNode Move() => Node("motion.move_forward",
            new BlockParam { key = "distance", kind = ParamKind.Number, number = 1f },
            new BlockParam { key = "duration", kind = ParamKind.Number, number = 0f });

        private static BlockNode Forever(params BlockNode[] body) =>
            new() { id = "e" + ++_nextId, blockType = "control.repeat_forever", branches = new[] { body } };

        private static BlockNode Stop(string target) => Node("control.stop", new BlockParam { key = "target", kind = ParamKind.Choice, text = target });
        private static BlockNode Broadcast(string message) => Node("event.broadcast", new BlockParam { key = "message", kind = ParamKind.Text, text = message });

        private static BlockStack Script(string trigger, params BlockNode[] sequence) =>
            new() { id = "stk_" + ++_nextId, triggerBlockType = trigger, sequence = sequence };

        private static BlockStack WhenReceived(string message, params BlockNode[] sequence) => new()
        {
            id = "stk_" + ++_nextId,
            triggerBlockType = "event.when_broadcast_received",
            triggerParameters = new[] { new BlockParam { key = "message", kind = ParamKind.Text, text = message } },
            sequence = sequence
        };

        /// <summary>An object running <paramref name="stacks"/>, its runner subscribed to the test broker.</summary>
        private GameObject Programmed(string name, params BlockStack[] stacks)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            _objects.Add(go);

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(new ObjectProgram { stacks = stacks });
            var runner = go.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            runner.Initialize();
            return go;
        }

        // ---- scripts ---------------------------------------------------------------------------------

        [Test]
        public void AScriptThatRunsToItsEnd_IsReportedStartedThenFinished_ThenAllFinished()
        {
            Programmed("Robot", Script("event.when_go_clicked"), Script("event.when_play_clicked", Move()));

            _broker.FirePlayClicked();
            _scheduler.Tick(0.1f);

            CollectionAssert.AreEqual(new[]
            {
                "started Robot #1 event.when_play_clicked",
                "finished Robot #1 Completed",
                "all finished"
            }, _heard);
        }

        [Test]
        public void AStopBlock_IsReportedAsStoppedByBlock_ForItselfAndTheScriptsItStops()
        {
            Programmed("Robot",
                Script("event.when_play_clicked", Forever(Move())),
                Script("event.when_go_clicked", Stop("all")));

            _broker.FirePlayClicked();
            _scheduler.Tick(0.1f);
            _heard.Clear();

            _broker.FireGoClicked();
            _scheduler.Tick(0.1f);

            CollectionAssert.AreEquivalent(new[]
            {
                "started Robot #1 event.when_go_clicked",
                "finished Robot #0 StoppedByBlock",
                "finished Robot #1 StoppedByBlock",
                "all finished"
            }, _heard);
        }

        [Test]
        public void AFailingBlock_IsReportedAsFailed()
        {
            Programmed("Robot", Script("event.when_play_clicked", Node("test.always_fails"), Move()));

            _broker.FirePlayClicked();
            _scheduler.Tick(0.1f);

            CollectionAssert.Contains(_heard, "finished Robot #0 Failed");
        }

        [Test]
        public void TheStopButton_IsReportedAsStopped_NotAsScriptsFinishing()
        {
            Programmed("Robot", Script("event.when_play_clicked", Forever(Move())));
            _broker.FirePlayClicked();
            _scheduler.Tick(0.1f);
            _heard.Clear();

            BlockyRuntime.Playback.Stop();
            _scheduler.Tick(0.1f);

            CollectionAssert.AreEqual(new[] { "stopped" }, _heard);
        }

        [Test]
        public void AScriptEndedByAnEdit_IsNotReportedAsFinished()
        {
            // The in-game editor shuts the runner down and starts it again on every change.
            var robot = Programmed("Robot", Script("event.when_play_clicked", Forever(Move())));
            _broker.FirePlayClicked();
            _scheduler.Tick(0.1f);
            _heard.Clear();

            robot.GetComponent<ObjectProgramRunner>().Shutdown();
            _scheduler.Tick(0.1f);

            CollectionAssert.IsEmpty(_heard);
        }

        [Test]
        public void Reset_IsReportedAsStoppedThenReset()
        {
            Programmed("Robot", Script("event.when_play_clicked", Move()));

            BlockyRuntime.Playback.ResetWorld();

            CollectionAssert.AreEqual(new[] { "stopped", "reset" }, _heard);
        }

        [Test]
        public void AHandlerThatThrows_IsLogged_AndStopsNeitherTheScriptsNorTheOtherHandlers()
        {
            void Throws(BlockyScript _) => throw new InvalidOperationException("a game's handler broke");
            BlockyEvents.ScriptStarted += Throws;
            BlockyEvents.ScriptStarted += OnStarted; // a second copy of the logging handler, after the thrower
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("a game's handler broke"));
                var robot = Programmed("Robot", Script("event.when_play_clicked", Move()));

                _broker.FirePlayClicked();
                _scheduler.Tick(0.1f);

                Assert.AreEqual(1f, robot.transform.position.z, 1e-4f, "the script still ran");
                Assert.AreEqual(2, _heard.FindAll(h => h.StartsWith("started")).Count, "both logging handlers heard it");
            }
            finally
            {
                BlockyEvents.ScriptStarted -= Throws;
                BlockyEvents.ScriptStarted -= OnStarted;
            }
        }

        // ---- level complete and messages ---------------------------------------------------------------

        [Test]
        public void TheLevelCompleteBlock_TellsGameCode_AndStartsEveryWhenLevelCompleteScript()
        {
            Programmed("Goal", Script("event.when_play_clicked", Node("event.level_complete")));
            var player = Programmed("Player", Script("event.when_level_complete", Move()));

            _broker.FirePlayClicked();
            _scheduler.Tick(0.1f);
            CollectionAssert.Contains(_heard, "level complete by Goal");
            Assert.AreEqual(0f, player.transform.position.z, 1e-4f, "its listeners start on the next tick, like a broadcast's");

            _scheduler.Tick(0.1f);

            Assert.AreEqual(1f, player.transform.position.z, 1e-4f);
        }

        [Test]
        public void CompleteLevel_FromCode_DoesWhatTheBlockDoes()
        {
            var player = Programmed("Player", Script("event.when_level_complete", Move()));

            BlockyEvents.CompleteLevel();
            _scheduler.Tick(0.1f);

            CollectionAssert.Contains(_heard, "level complete by code");
            Assert.AreEqual(1f, player.transform.position.z, 1e-4f);
        }

        [Test]
        public void Broadcast_FromCode_StartsWhenIReceiveScripts_AndBlocksBroadcastsReachCode()
        {
            var door = Programmed("Door", WhenReceived("open", Move()));
            Programmed("Button", WhenReceived("pressed", Broadcast("Open")));

            BlockyEvents.Broadcast("pressed");
            _scheduler.Tick(0.1f); // the button hears "pressed" and broadcasts "Open"
            _scheduler.Tick(0.1f); // the door hears it

            CollectionAssert.Contains(_heard, "message pressed");
            CollectionAssert.Contains(_heard, "message Open");
            Assert.AreEqual(1f, door.transform.position.z, 1e-4f);
        }
    }
}
