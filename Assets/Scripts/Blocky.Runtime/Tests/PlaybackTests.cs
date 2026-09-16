using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>The run bar's commands end to end: a real runner, broker, scheduler and world snapshot.</summary>
    public class PlaybackTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("PlaybackTestTarget");
            _target.SetActive(false); // OnEnable must not fire before dependencies are wired via BlockyRuntime.SetForTests
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            BlockyRuntime.Reset();
        }

        private static BlockDefinition Def(string blockType, BlockShape shape, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

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

        /// <summary>A move that takes 100 script-seconds and goes nowhere — a stand-in for the demo's never-ending SpinnerCube.</summary>
        private static BlockNode NeverEndingMove(string id) => new()
        {
            id = id,
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = 0f },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = 100f }
            }
        };

        /// <summary>"when Play clicked → move 1, move 1, move 1" (or <paramref name="stacks"/>) on the target, wired into <see cref="BlockyRuntime"/>.</summary>
        private Playback SetUpScript(BlockStack[] stacks = null)
        {
            var registry = BlockRegistry.Build(new[]
            {
                Def("event.when_play_clicked", BlockShape.Trigger),
                Def("event.when_key_pressed", BlockShape.Trigger),
                Def("event.when_go_clicked", BlockShape.Trigger),
                Def("motion.move_forward", BlockShape.Statement, new[]
                {
                    new ParamSpec { key = "distance", kind = ParamKind.Number, min = -1000, max = 1000 },
                    new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
                })
            });
            BlockyRuntime.SetForTests(registry, new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly)), new TriggerBroker());

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(new ObjectProgram
            {
                stacks = stacks ?? new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = new[] { Move("n1"), Move("n2"), Move("n3") } } }
            });
            var runner = _target.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            runner.Initialize(); // also records the target's start in BlockyRuntime.World

            return BlockyRuntime.Playback;
        }

        private static void Tick() => BlockyRuntime.Scheduler.Tick(0.1f);

        private float Z => _target.transform.position.z;

        [Test]
        public void StepForwardAndBack_WalkThroughTheProgram_AndPutTheObjectBack()
        {
            var playback = SetUpScript();

            playback.StepForward();
            Tick();
            Assert.AreEqual(1f, Z, 1e-4f);

            playback.StepForward();
            Tick();
            Assert.AreEqual(2f, Z, 1e-4f);

            playback.StepBack();
            Assert.AreEqual(1f, Z, 1e-4f);
            Assert.IsTrue(playback.IsPaused);

            playback.StepForward();
            Tick();
            Assert.AreEqual(2f, Z, 1e-4f);
        }

        [Test]
        public void StepBackPastTheFirstStep_ReturnsToBeforeItStarted_AndStepForwardStartsItAgain()
        {
            var playback = SetUpScript();
            playback.StepForward();
            Tick();

            playback.StepBack();
            Assert.AreEqual(0f, Z, 1e-4f);
            Assert.IsFalse(playback.IsRunning);
            Assert.IsFalse(playback.CanStepBack);

            playback.StepForward(); // "when Play clicked" was re-armed, so this starts the script again
            Tick();
            Assert.AreEqual(1f, Z, 1e-4f);
        }

        [Test]
        public void Go_RunsNormally_AndForgetsTheSteps()
        {
            var playback = SetUpScript();
            playback.StepForward();
            Tick();
            Assert.IsTrue(playback.CanStepBack);

            playback.Go();
            Tick();

            Assert.IsFalse(playback.CanStepBack);
            Assert.IsFalse(playback.IsPaused);
            Assert.AreEqual(3f, Z, 1e-4f);
        }

        [Test]
        public void Reset_PutsTheObjectBack_ThenGoReplaysFromTheStart()
        {
            var playback = SetUpScript();
            playback.Go();
            Tick();
            Assert.AreEqual(3f, Z, 1e-4f);

            playback.ResetWorld();
            Assert.AreEqual(0f, Z, 1e-4f);
            Assert.IsFalse(playback.IsRunning);

            playback.Go();
            Tick();
            Assert.AreEqual(3f, Z, 1e-4f);
        }

        [Test]
        public void Stop_ThenGo_RunsThePlayClickedScriptAgain_FromWhereTheObjectIs()
        {
            var playback = SetUpScript();
            playback.Go();
            Tick();
            Assert.AreEqual(3f, Z, 1e-4f);

            playback.Stop();
            playback.Go(); // "when Play clicked" fires again; nothing is put back, so it carries on from z = 3
            Tick();

            Assert.AreEqual(6f, Z, 1e-4f);
        }

        [Test]
        public void Stop_ThenStepForward_StartsThePlayClickedScript()
        {
            var playback = SetUpScript();
            playback.Go();
            Tick();
            playback.Stop();

            playback.StepForward();
            Tick();

            Assert.AreEqual(4f, Z, 1e-4f);
            Assert.IsTrue(playback.IsRunning);
        }

        [Test]
        public void StepForward_AfterTheScriptRanToItsEnd_StartsItAgain()
        {
            var playback = SetUpScript();
            BlockyRuntime.Triggers.FirePlayClicked(); // the scene started it, as BlockyRuntimeTicker does
            Tick();
            Assert.AreEqual(3f, Z, 1e-4f);
            Assert.IsFalse(playback.IsRunning);

            playback.StepForward();
            Tick();

            Assert.AreEqual(4f, Z, 1e-4f);
            Assert.IsTrue(playback.IsPaused);
            Assert.IsTrue(playback.CanStepBack);
        }

        [Test]
        public void Go_AfterTheScriptRanToItsEnd_RunsItAgain()
        {
            var playback = SetUpScript();
            BlockyRuntime.Triggers.FirePlayClicked();
            Tick();

            playback.Go();
            Tick();

            Assert.AreEqual(6f, Z, 1e-4f);
        }

        [Test]
        public void IsGoing_OnlyAfterGo_AndEndsWhenTheScriptsFinish()
        {
            var playback = SetUpScript();
            BlockyRuntime.Triggers.FirePlayClicked();
            Assert.IsTrue(playback.IsRunning);
            Assert.IsFalse(playback.IsGoing); // started by the scene, not by Go

            playback.Stop();
            playback.Go();
            Assert.IsTrue(playback.IsGoing);

            Tick(); // three instant moves: finished within one tick
            Assert.IsFalse(playback.IsGoing);

            playback.Go();
            playback.Stop();
            Assert.IsFalse(playback.IsGoing);
        }

        [Test]
        public void StepForward_WithNoScriptToStart_DoesNotPause()
        {
            var playback = SetUpScript(new BlockStack[0]);

            playback.StepForward();

            Assert.IsFalse(playback.IsPaused);
            Assert.IsFalse(playback.CanStepBack);
        }

        /// <summary>
        /// The learner can't press a key while the scene is frozen between steps, so Step forward starts a
        /// "when key pressed" script by hand — the same for "when collided" and "when looked at".
        /// </summary>
        [Test]
        public void StepForward_StartsAScriptUnderAnyEventHat_NotJustPlayClicked()
        {
            var playback = SetUpScript(new[]
            {
                new BlockStack { id = "stk_1", triggerBlockType = "event.when_key_pressed", sequence = new[] { Move("n1"), Move("n2") } }
            });

            playback.StepForward();
            Tick();

            Assert.AreEqual(1f, Z, 1e-4f); // one block of the key script ran, with no key ever pressed
            Assert.IsTrue(playback.IsPaused);
            Assert.IsTrue(playback.CanStepBack);
        }

        /// <summary>
        /// Regression: Step only started scripts when the *whole scene* was idle. The demo scene has a
        /// <c>repeat forever</c> spinner, so it never was — and Step appeared to do nothing to every other script.
        /// </summary>
        [Test]
        public void StepForward_StartsAnIdleScript_WhileAnotherScriptIsStillRunning()
        {
            var playback = SetUpScript(new[]
            {
                new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = new[] { NeverEndingMove("n1") } },
                new BlockStack { id = "stk_2", triggerBlockType = "event.when_key_pressed", sequence = new[] { Move("n2") } }
            });
            BlockyRuntime.Triggers.FirePlayClicked(); // the scene starts the never-ending script, as it does in the demo
            Tick();
            Assert.IsTrue(playback.IsRunning);
            Assert.AreEqual(0f, Z, 1e-4f);

            playback.StepForward();
            Tick();

            Assert.AreEqual(1f, Z, 1e-4f); // the key script started even though the scene was never idle
        }

        /// <summary>A script already part-way through must not jump back to its first block when Step is pressed again.</summary>
        [Test]
        public void StepForward_DoesNotRestartAScriptItIsAlreadyWalking()
        {
            var playback = SetUpScript();

            playback.StepForward();
            Tick();
            playback.StepForward();
            Tick();
            playback.StepForward();
            Tick();

            Assert.AreEqual(3f, Z, 1e-4f); // one block per press, straight through — not the first block three times
        }

        [Test]
        public void SetSpeed_StaysBetween1And4_AndSpeedsUpScriptTime()
        {
            var playback = SetUpScript();

            playback.SetSpeed(3);
            Assert.AreEqual(3, playback.Speed);
            Assert.AreEqual(3f, BlockyRuntime.Scheduler.TimeScale);

            playback.SetSpeed(9);
            Assert.AreEqual(Playback.MaxSpeed, playback.Speed);

            playback.SetSpeed(0);
            Assert.AreEqual(1, playback.Speed);
        }
    }
}
