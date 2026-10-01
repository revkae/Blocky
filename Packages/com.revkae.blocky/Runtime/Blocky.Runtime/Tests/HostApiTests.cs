using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Blocky.Runtime.Tests
{
    /// <summary>
    /// What a game that hosts Blocky leans on in the runtime (ADR-041): its own program storage, a program handed to a
    /// runner, a ticker whose channels can be switched off, "when looked at" raised by the game, and more than one
    /// say in what counts as a press on the game.
    /// </summary>
    public class HostApiTests
    {
        /// <summary>A game's storage: programs kept in memory by object, and a record of what was asked.</summary>
        private sealed class FakeStorage : IProgramStorage
        {
            public readonly Dictionary<GameObject, ObjectProgram> Programs = new();
            public readonly HashSet<GameObject> Unreadable = new();
            public readonly List<GameObject> Loads = new();
            public bool HasSaves = true;

            public bool HasSavesFor(Scene scene) => HasSaves;

            public bool Exists(GameObject owner) => Programs.ContainsKey(owner);

            public SavedProgramStatus TryLoad(GameObject owner, out ObjectProgram program)
            {
                Loads.Add(owner);
                program = null;
                if (Unreadable.Contains(owner)) return SavedProgramStatus.Unreadable;
                return Programs.TryGetValue(owner, out program) ? SavedProgramStatus.Loaded : SavedProgramStatus.None;
            }

            public bool TrySave(GameObject owner, ObjectProgram program, out string problem)
            {
                Programs[owner] = program;
                problem = null;
                return true;
            }
        }

        private readonly List<Object> _made = new();
        private GameObject _target;
        private FakeStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _target = Make("HostApiTarget");
            _target.SetActive(false); // a runner added to it must not start before the fixtures are wired
            _storage = new FakeStorage();
            BlockyStorage.Current = _storage; // never the player's real save folder: a ticker would hand runners to the open scene's objects
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made)
                if (made != null) Object.DestroyImmediate(made);
            _made.Clear();
            BlockyStorage.Current = null;
            BlockyInput.SetForTests(null);
            BlockyRuntime.Reset();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            _made.Add(go);
            return go;
        }

        private static BlockDefinition Def(string blockType, BlockShape shape, params ParamSpec[] parameters)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.parameters = parameters;
            return def;
        }

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("event.when_key_pressed", BlockShape.Trigger, new ParamSpec { key = "key", kind = ParamKind.Text }),
            Def("event.when_clicked", BlockShape.Trigger),
            Def("event.when_looked_at", BlockShape.Trigger, new ParamSpec { key = "angle_threshold", kind = ParamKind.Number, min = 0, max = 180 }),
            Def("motion.move_forward", BlockShape.Statement,
                new ParamSpec { key = "distance", kind = ParamKind.Number, min = -1000, max = 1000 },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 })
        });

        private static BlockNode Move(float distance, float duration = 0f) => new()
        {
            id = "n_move",
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = distance },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = duration }
            }
        };

        private static ObjectProgram Program(string trigger, float distance, float duration = 0f, params BlockParam[] triggerParameters) => new()
        {
            stacks = new[]
            {
                new BlockStack { id = "stk_" + trigger, triggerBlockType = trigger, triggerParameters = triggerParameters, sequence = new[] { Move(distance, duration) } }
            }
        };

        private static void WireRuntime()
        {
            var registry = BuildRegistry();
            BlockyRuntime.SetForTests(registry, new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly)), new TriggerBroker());
        }

        private static ObjectProgramRunner Runner(GameObject on, ObjectProgram program)
        {
            var runner = on.AddComponent<ObjectProgramRunner>();
            runner.SetProgram(program);
            runner.Initialize();
            return runner;
        }

        private static void Tick() => BlockyRuntime.Scheduler.Tick(1f);

        private static float Z(GameObject go) => go.transform.position.z;

        // ---- storage (#4) --------------------------------------------------------------------------------

        [Test]
        public void TheStorage_IsTheSaveFiles_UntilAGameSetsItsOwn_AndAgainAfterNull()
        {
            Assert.AreSame(_storage, BlockyStorage.Current);

            BlockyStorage.Current = null;

            Assert.AreSame(FileProgramStorage.Instance, BlockyStorage.Current);
        }

        [Test]
        public void ARunner_RunsTheProgramItsGamesStorageHas()
        {
            WireRuntime();
            _storage.Programs[_target] = Program("event.when_play_clicked", 3f);
            var runner = _target.AddComponent<ObjectProgramRunner>();

            runner.Initialize();
            BlockyRuntime.Triggers.FirePlayClicked();
            Tick();

            Assert.AreEqual(3f, Z(_target), 1e-4f);
            Assert.AreEqual(SavedProgramStatus.Loaded, runner.SavedProgram);
            Assert.AreEqual(1, _storage.Loads.Count, "looked for once, however often the program is read");
        }

        [Test]
        public void AStorageWithNothingForTheScene_IsNeverAskedAboutAnObject()
        {
            _storage.HasSaves = false;
            var runner = _target.AddComponent<ObjectProgramRunner>();

            Assert.AreEqual(0, runner.LoadProgram().stacks.Length);
            CollectionAssert.IsEmpty(_storage.Loads);
        }

        [Test]
        public void AnUnreadableSave_LeavesTheSceneProgramRunning_AndTheRunnerSaysSo()
        {
            _storage.Unreadable.Add(_target);
            var sceneAsset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            _made.Add(sceneAsset);
            sceneAsset.Save(Program("event.when_play_clicked", 1f));
            var runner = _target.AddComponent<ObjectProgramRunner>();
            typeof(ObjectProgramRunner).GetField("programAsset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(runner, sceneAsset); // as the scene would have it: not handed over by code

            Assert.AreEqual("stk_event.when_play_clicked", runner.LoadProgram().stacks[0].id);
            Assert.AreEqual(SavedProgramStatus.Unreadable, runner.SavedProgram);
        }

        [Test]
        public void AttachToSavedObjects_AsksAGamesStorageAboutEachObject()
        {
            var saved = Make("HostApiSaved");
            var child = Make("HostApiSavedChild");
            child.transform.SetParent(saved.transform, false);
            var untouched = Make("HostApiUntouched");
            saved.SetActive(false); // the runners added must not start
            _storage.Programs[saved] = Program("event.when_play_clicked", 1f);
            _storage.Programs[child] = Program("event.when_play_clicked", 1f);

            Assert.AreEqual(2, ObjectProgramRunner.AttachToSavedObjects(saved.scene));
            Assert.IsNotNull(saved.GetComponent<ObjectProgramRunner>());
            Assert.IsNotNull(child.GetComponent<ObjectProgramRunner>());
            Assert.IsNull(untouched.GetComponent<ObjectProgramRunner>());
            Assert.AreEqual(0, ObjectProgramRunner.AttachToSavedObjects(saved.scene), "running it again adds nothing");
        }

        [Test]
        public void TheSaveFiles_AreReachedByObject_ThroughTheDefaultStorage()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BlockyHostApiTests_" + System.Guid.NewGuid().ToString("N"));
            RuntimeProgramStorage.RootOverride = root;
            try
            {
                var files = FileProgramStorage.Instance;
                Assert.IsFalse(files.Exists(_target));
                Assert.AreEqual(SavedProgramStatus.None, files.TryLoad(_target, out _));

                Assert.IsTrue(files.TrySave(_target, Program("event.when_play_clicked", 1f), out var problem), problem);

                Assert.IsTrue(files.HasSavesFor(_target.scene));
                Assert.IsTrue(files.Exists(_target));
                Assert.AreEqual(SavedProgramStatus.Loaded, files.TryLoad(_target, out var loaded));
                Assert.AreEqual("stk_event.when_play_clicked", loaded.stacks[0].id);
                Assert.IsTrue(RuntimeProgramStorage.Exists(RuntimeProgramStorage.KeyFor(_target)), "the same file the 0.1.0 key names");
            }
            finally
            {
                RuntimeProgramStorage.RootOverride = null;
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
            }
        }

        // ---- SetProgram (#12) ----------------------------------------------------------------------------

        [Test]
        public void SetProgram_OnARunningRunner_StopsItsScripts_AndRunsTheNewProgramAtOnce()
        {
            WireRuntime();
            var runner = Runner(_target, Program("event.when_play_clicked", 100f, 100f));
            BlockyRuntime.Triggers.FirePlayClicked();
            Tick();
            Assert.AreEqual(1f, Z(_target), 1e-4f, "one second into a slow move");

            runner.SetProgram(Program("event.when_key_pressed", 5f, 0f, new BlockParam { key = "key", kind = ParamKind.Text, text = "Space" }));
            Tick();
            Assert.AreEqual(1f, Z(_target), 1e-4f, "the old script was stopped where it was");

            BlockyRuntime.Playback.StepForward(_target); // Step starts any hat by hand
            BlockyRuntime.Scheduler.Tick(0.1f);
            Assert.AreEqual(6f, Z(_target), 1e-4f, "the new program is compiled and subscribed, with nothing else to call");
        }

        [Test]
        public void SetProgram_BeforeTheRunnerStarts_IsWhatItRuns_OverASavedProgram()
        {
            WireRuntime();
            _storage.Programs[_target] = Program("event.when_play_clicked", 9f);
            var runner = _target.AddComponent<ObjectProgramRunner>();

            runner.SetProgram(Program("event.when_play_clicked", 2f));
            runner.Initialize();
            BlockyRuntime.Triggers.FirePlayClicked();
            Tick();

            Assert.AreEqual(2f, Z(_target), 1e-4f);
            Assert.AreEqual(SavedProgramStatus.None, runner.SavedProgram);
            CollectionAssert.IsEmpty(_storage.Loads, "a program handed over by code wins: the storage is never asked");
        }

        [Test]
        public void SetProgram_ReusesOneAsset_AndCopiesTheProgram()
        {
            WireRuntime();
            var runner = _target.AddComponent<ObjectProgramRunner>();
            var program = Program("event.when_play_clicked", 1f);

            runner.SetProgram(program);
            var asset = runner.ProgramAsset;
            program.stacks[0].id = "changed_afterwards";
            runner.SetProgram(null);

            Assert.AreSame(asset, runner.ProgramAsset);
            Assert.AreEqual(0, runner.LoadProgram().stacks.Length, "null is an empty program");
        }

        // ---- the ticker's switches (#5) ------------------------------------------------------------------

        private BlockyRuntimeTicker Ticker() => Make("HostApiTicker").AddComponent<BlockyRuntimeTicker>();

        [Test]
        public void TheTicker_StartsWhenPlayClickedScripts_UnlessToldNotTo_AndGoStillRunsThem()
        {
            WireRuntime();
            Runner(_target, Program("event.when_play_clicked", 1f));
            var ticker = Ticker();
            ticker.FirePlayClickedOnStart = false;

            ticker.Begin();
            Tick();
            Assert.AreEqual(0f, Z(_target), 1e-4f, "the game starts scripts itself");

            BlockyRuntime.Playback.Go();
            Tick();
            Assert.AreEqual(1f, Z(_target), 1e-4f);
        }

        [Test]
        public void TheTicker_StartsWhenPlayClickedScripts_ByDefault()
        {
            WireRuntime();
            Runner(_target, Program("event.when_play_clicked", 1f));

            Ticker().Begin();
            Tick();

            Assert.AreEqual(1f, Z(_target), 1e-4f);
        }

        [Test]
        public void TheTicker_GivesSavedObjectsARunner_UnlessToldNotTo()
        {
            WireRuntime();
            var saved = Make("HostApiSavedForTicker");
            saved.SetActive(false);
            _storage.Programs[saved] = Program("event.when_play_clicked", 1f);

            var off = Ticker();
            off.AttachSavedPrograms = false;
            off.Begin();
            Assert.IsNull(saved.GetComponent<ObjectProgramRunner>());

            Ticker().Begin();
            Assert.IsNotNull(saved.GetComponent<ObjectProgramRunner>());
        }

        [Test]
        public void TheTicker_ReadsTheKeyboard_UnlessToldNotTo()
        {
            WireRuntime();
            var pressed = 0;
            BlockyRuntime.Triggers.OnKeyPressed += key => { if (key == Key.Space) pressed++; };
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                var ticker = Ticker();
                ticker.PollKeyboard = false;
                InputState.Change(keyboard, new KeyboardState(Key.Space));
                ticker.Poll(keyboard, null);
                Assert.AreEqual(0, pressed, "switched off, the key is never seen");

                ticker.PollKeyboard = true;
                ticker.Poll(keyboard, null);
                Assert.AreEqual(1, pressed);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void TheTicker_DecidesWhatIsLookedAt_UnlessToldNotTo()
        {
            WireRuntime();
            var camera = Make("HostApiCamera").AddComponent<Camera>();
            var looked = 0;
            BlockyRuntime.Triggers.RegisterLookedAt(_target, 15f);
            BlockyRuntime.Triggers.OnLookedAt += _ => looked++;
            _target.transform.position = camera.transform.position + camera.transform.forward * 10f;

            var ticker = Ticker();
            ticker.PollLookedAt = false;
            ticker.Poll(null, camera);
            Assert.AreEqual(0, looked, "switched off, the game decides — and raises it itself");

            ticker.PollLookedAt = true;
            ticker.Poll(null, camera);
            Assert.AreEqual(1, looked);
        }

        [Test]
        public void TheTicker_DecidesWhatIsClicked_UnlessToldNotTo()
        {
            Assume.That(Pointer.current, Is.Not.Null, "this machine has no pointer for the click poll to read");
            WireRuntime();

            // Far from anything in the open scene, facing a wall no ray from this camera can miss.
            var camera = Make("HostApiCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(50000f, 50000f, 50000f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _made.Add(wall);
            wall.transform.position = camera.transform.position + camera.transform.forward * 5f;
            wall.transform.localScale = new Vector3(100000f, 100000f, 1f);
            Physics.SyncTransforms();

            var clicked = 0;
            BlockyRuntime.Triggers.OnClicked += _ => clicked++;
            BlockyInput.SetForTests(() => true); // the button is held

            var ticker = Ticker();
            ticker.PollClicked = false;
            ticker.Poll(null, camera);
            Assert.AreEqual(0, clicked, "switched off, a press is not a click on anything");

            ticker.PollClicked = true;
            ticker.Poll(null, camera);
            Assert.AreEqual(1, clicked);
        }

        // ---- "when looked at", raised by the game (#6) ---------------------------------------------------

        [Test]
        public void RaiseLookedAt_StartsOnlyThatObjectsScripts_AndRaisedAgainMidRunRestartsThem()
        {
            WireRuntime();
            var other = Make("HostApiOther");
            other.SetActive(false);
            var threshold = new BlockParam { key = "angle_threshold", kind = ParamKind.Number, number = 15f };
            Runner(_target, Program("event.when_looked_at", 10f, 10f, threshold));
            Runner(other, Program("event.when_looked_at", 10f, 10f, threshold));
            var broker = BlockyRuntime.Triggers;

            broker.RaiseLookedAt(_target);
            Tick();
            Tick();
            Assert.AreEqual(2f, Z(_target), 1e-4f);
            Assert.AreEqual(0f, Z(other), 1e-4f, "nobody looked at the other one");

            broker.RaiseLookedAt(_target); // looked at again, two seconds into a ten-second move
            for (var i = 0; i < 12; i++) Tick();

            // Ignored, it would have stopped at 10; run beside the first, it would have gone further than 12.
            Assert.AreEqual(12f, Z(_target), 1e-3f, "the script started over: two units from the first run, then the whole move");
        }

        // ---- what counts as a press on the game (#11) ----------------------------------------------------

        [Test]
        public void APress_IsNotOnTheGame_WhileAnyAddedCheckSaysThePointerIsOverUi()
        {
            BlockyInput.SetForTests(() => true);
            var overGameUi = false;
            var overEditor = false;
            bool GameUi() => overGameUi;
            bool Editor() => overEditor;
            BlockyInput.AddPointerOverUiCheck(GameUi);
            BlockyInput.AddPointerOverUiCheck(Editor);
            try
            {
                Assert.IsTrue(BlockyInput.IsMouseDown);

                overGameUi = true;
                Assert.IsFalse(BlockyInput.IsMouseDown, "a press on the game's own interface");

                overGameUi = false;
                overEditor = true;
                Assert.IsFalse(BlockyInput.IsMouseDown, "each check has its say");

                BlockyInput.RemovePointerOverUiCheck(Editor);
                Assert.IsTrue(BlockyInput.IsMouseDown, "a removed check is no longer asked");

                BlockyInput.IsPointerOverUi = () => true;
                Assert.IsFalse(BlockyInput.IsMouseDown, "the single slot from 0.1.0 still counts");
            }
            finally
            {
                BlockyInput.IsPointerOverUi = null;
                BlockyInput.RemovePointerOverUiCheck(GameUi);
                BlockyInput.RemovePointerOverUiCheck(Editor);
            }
        }
    }
}
