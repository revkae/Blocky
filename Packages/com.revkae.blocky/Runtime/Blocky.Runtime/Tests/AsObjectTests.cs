using System.Text.RegularExpressions;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blocky.Runtime.Tests
{
    /// <summary>
    /// <c>as [object]</c> (ADR-042): the blocks in its mouth act on another object, the script's own object is back
    /// after it, and the run bar's Reset and Step back cover the object that was acted on.
    /// </summary>
    public class AsObjectTests
    {
        private GameObject _a;
        private GameObject _b;
        private GameObject _c;
        private int _nextId;

        [SetUp]
        public void SetUp()
        {
            _nextId = 0;
            _a = new GameObject("AsTestA");
            _b = new GameObject("AsTestB");
            _c = new GameObject("AsTestC");
            _a.SetActive(false); // a runner added to it must not start before the fixtures are wired
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { _a, _b, _c })
                if (go != null) Object.DestroyImmediate(go);
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

        private static ParamSpec Num(string key) => new() { key = key, kind = ParamKind.Number, min = -1000000, max = 1000000 };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("motion.move_forward", BlockShape.Statement, 0, Num("distance"), Num("duration")),
            Def("control.repeat", BlockShape.CBlock, 1, Num("times")),
            Def("control.as_object", BlockShape.CBlock, 1, new ParamSpec { key = "object", kind = ParamKind.ObjectRef }),
            Def("control.stop", BlockShape.Statement, 0, new ParamSpec
            {
                key = "target",
                kind = ParamKind.Choice,
                choices = new[]
                {
                    new ChoiceEntry { stableId = "this_script" },
                    new ChoiceEntry { stableId = "all" },
                    new ChoiceEntry { stableId = "other_scripts" }
                }
            })
        });

        private string Id() => "n" + ++_nextId;

        private BlockNode Move(float distance, float duration = 0f) => new()
        {
            id = Id(),
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = distance },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = duration }
            }
        };

        private BlockNode As(string name, params BlockNode[] body) => new()
        {
            id = Id(),
            blockType = "control.as_object",
            parameters = new[] { new BlockParam { key = "object", kind = ParamKind.ObjectRef, text = name } },
            branches = new[] { body }
        };

        private BlockNode Repeat(int times, params BlockNode[] body) => new()
        {
            id = Id(),
            blockType = "control.repeat",
            parameters = new[] { new BlockParam { key = "times", kind = ParamKind.Number, number = times } },
            branches = new[] { body }
        };

        private BlockNode StopOtherScripts() => new()
        {
            id = Id(),
            blockType = "control.stop",
            parameters = new[] { new BlockParam { key = "target", kind = ParamKind.Choice, text = "other_scripts" } }
        };

        private static BlockStack Script(string id, params BlockNode[] sequence) =>
            new() { id = id, triggerBlockType = "event.when_play_clicked", sequence = sequence };

        /// <summary>Runs <paramref name="sequence"/> as a script of <see cref="_a"/> for <paramref name="ticks"/> one-second ticks.</summary>
        private VmThread Run(int ticks, params BlockNode[] sequence)
        {
            var registry = BuildRegistry();
            var result = ProgramCompiler.Link(new ObjectProgram { stacks = new[] { Script("stk_1", sequence) } }, registry);
            CollectionAssert.IsEmpty(result.Diagnostics);

            BlockyRuntime.Objects.Register(_b);
            BlockyRuntime.Objects.Register(_c);

            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            var thread = scheduler.Start(result.Program, _a, result.Program.StackEntryPoints[0]);
            for (var i = 0; i < ticks; i++) scheduler.Tick(1f);
            return thread;
        }

        /// <summary><paramref name="stacks"/> as the program of a runner on <see cref="_a"/>, wired into <see cref="BlockyRuntime"/> as in play.</summary>
        private void SetUpRunner(params BlockStack[] stacks)
        {
            var registry = BuildRegistry();
            BlockyRuntime.SetForTests(registry, new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly)), new TriggerBroker());
            BlockyRuntime.Objects.Register(_b);
            BlockyRuntime.Objects.Register(_c);

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(new ObjectProgram { stacks = stacks });
            var runner = _a.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            runner.Initialize();
        }

        private static float Z(GameObject go) => go.transform.position.z;

        [Test]
        public void BlocksInside_MoveTheNamedObject_NotTheOneRunningTheScript()
        {
            Run(1, As("AsTestB", Move(1f)));

            Assert.AreEqual(1f, Z(_b), 1e-4f);
            Assert.AreEqual(0f, Z(_a), 1e-4f);
        }

        [Test]
        public void AfterTheBlock_BlocksActOnTheScriptsOwnObjectAgain()
        {
            var thread = Run(1, As("AsTestB", Move(1f)), Move(2f));

            Assert.AreEqual(1f, Z(_b), 1e-4f);
            Assert.AreEqual(2f, Z(_a), 1e-4f);
            Assert.AreSame(_a, thread.ActsOn);
            Assert.AreEqual(0, thread.FrameCount, "the block's frame is gone");
        }

        [Test]
        public void Nested_EachLevelPutsBackTheObjectOfTheLevelOutside()
        {
            Run(1, As("AsTestB", Move(1f), As("AsTestC", Move(5f)), Move(1f)), Move(3f));

            Assert.AreEqual(2f, Z(_b), 1e-4f, "b moves before and after the inner block");
            Assert.AreEqual(5f, Z(_c), 1e-4f);
            Assert.AreEqual(3f, Z(_a), 1e-4f);
        }

        [Test]
        public void Me_InsideTheBlock_IsStillAnObject_AndAnEmptyBlockIsHarmless()
        {
            var thread = Run(1, As("me", Move(1f)), As("AsTestB"), Move(1f));

            Assert.AreEqual(2f, Z(_a), 1e-4f);
            Assert.AreEqual(ScriptEndReason.Completed, thread.EndReason);
        }

        [Test]
        public void AnUnknownName_SkipsTheBlocksInside_SaysSoOnce_AndTheScriptCarriesOn()
        {
            var warnings = 0;
            void Count(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && message.Contains("found no object named 'Nobody'")) warnings++;
            }

            Application.logMessageReceived += Count;
            try
            {
                var thread = Run(10, Repeat(3, As("Nobody", Move(1f))), Move(2f));

                Assert.AreEqual(2f, Z(_a), 1e-4f, "the blocks inside never ran, on anything; the block after did");
                Assert.AreEqual(ScriptEndReason.Completed, thread.EndReason);
                Assert.AreEqual(1, warnings, "three laps, one warning");
            }
            finally
            {
                Application.logMessageReceived -= Count;
            }
        }

        [Test]
        public void TheObjectDestroyedWhileItsBlocksRun_SkipsTheRestOfThem_AndTheScriptCarriesOn()
        {
            LogAssert.Expect(LogType.Warning, new Regex("was destroyed while its blocks ran"));
            var registry = BuildRegistry();
            var program = new ObjectProgram { stacks = new[] { Script("stk_1", As("AsTestB", Repeat(2, Move(1f, 10f)), Move(1f)), Move(2f)) } };
            var result = ProgramCompiler.Link(program, registry);
            BlockyRuntime.Objects.Register(_b);

            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            var thread = scheduler.Start(result.Program, _a, result.Program.StackEntryPoints[0]);
            scheduler.Tick(1f);
            Assert.Greater(Z(_b), 0f, "b is on its way, inside a loop inside the block");

            Object.DestroyImmediate(_b);
            for (var i = 0; i < 3; i++) scheduler.Tick(1f);

            Assert.AreEqual(2f, Z(_a), 1e-4f, "the script left the block and ran what follows it, on its own object");
            Assert.AreEqual(ScriptEndReason.Completed, thread.EndReason, "it ended by running out of blocks, not by failing");
        }

        [Test]
        public void StopOtherScripts_InsideTheBlock_StillMeansTheScriptsOwnObject()
        {
            SetUpRunner(
                Script("stk_stopper", As("AsTestB", StopOtherScripts())),
                Script("stk_long", Move(100f, 100f)));
            var scheduler = BlockyRuntime.Scheduler;
            var programOfB = ProgramCompiler.Link(new ObjectProgram { stacks = new[] { Script("stk_b", Move(100f, 100f)) } }, BlockyRuntime.Registry).Program;
            var onB = scheduler.Start(programOfB, _b, programOfB.StackEntryPoints[0]);

            BlockyRuntime.Triggers.FirePlayClicked();
            scheduler.Tick(1f);
            scheduler.Tick(1f);

            Assert.IsFalse(scheduler.HasWorkOn(_a), "a's other script was stopped, and the stopper ran out of blocks");
            Assert.AreNotEqual(ThreadState.Done, onB.State, "b's own script was left alone");
        }

        [Test]
        public void Reset_PutsTheOtherObjectBackToo_ForTheWholeSceneAndForTheOneObject()
        {
            SetUpRunner(Script("stk_1", As("AsTestB", Move(1f))));
            var playback = BlockyRuntime.Playback;

            BlockyRuntime.Triggers.FirePlayClicked();
            BlockyRuntime.Scheduler.Tick(1f);
            Assert.AreEqual(1f, Z(_b), 1e-4f);

            playback.ResetWorld(_a); // the run bar's Reset, for the object open in the editor
            Assert.AreEqual(0f, Z(_b), 1e-4f, "resetting a puts back what a's script moved");

            playback.Go();
            BlockyRuntime.Scheduler.Tick(1f);
            Assert.AreEqual(1f, Z(_b), 1e-4f);

            playback.ResetWorld();
            Assert.AreEqual(0f, Z(_b), 1e-4f);
        }

        [Test]
        public void StepBack_PutsTheOtherObjectBackToo()
        {
            SetUpRunner(Script("stk_1", As("AsTestB", Move(1f), Move(1f))));
            var playback = BlockyRuntime.Playback;
            var scheduler = BlockyRuntime.Scheduler;

            playback.StepForward(_a); // the "as" block itself
            scheduler.Tick(0.1f);
            playback.StepForward(_a); // the first move
            scheduler.Tick(0.1f);
            Assert.AreEqual(1f, Z(_b), 1e-4f);

            playback.StepForward(_a); // the second move
            scheduler.Tick(0.1f);
            Assert.AreEqual(2f, Z(_b), 1e-4f);

            playback.StepBack(_a);
            Assert.AreEqual(1f, Z(_b), 1e-4f, "stepping a back undoes what its last block did to b");
        }
    }
}
