using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>Custom blocks (ADR-029): <c>define</c>, <c>run</c> and <c>input a/b/c</c>, and the call frames under them.</summary>
    public class CustomBlockTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("BlockyCustomBlockTarget");

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            BlockyRuntime.Reset(); // variables live on BlockyRuntime
        }

        // ---- a small catalog -------------------------------------------------------------------------

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

        private static ParamSpec Num(string key) => new() { key = key, kind = ParamKind.Number, min = -100000, max = 100000 };
        private static ParamSpec Str(string key) => new() { key = key, kind = ParamKind.Text };
        private static ParamSpec Rep(string key) => new() { key = key, kind = ParamKind.Reporter };

        private static ParamSpec Choices(string key, params string[] ids)
        {
            var entries = new ChoiceEntry[ids.Length];
            for (var i = 0; i < ids.Length; i++) entries[i] = new ChoiceEntry { stableId = ids[i], displayNameKey = ids[i] };
            return new ParamSpec { key = key, kind = ParamKind.Choice, defaultText = ids[0], choices = entries };
        }

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def(CustomBlocks.DefineType, BlockShape.Trigger, 0, Str("name")),
            Def(CustomBlocks.RunType, BlockShape.Statement, 0, Str("name"), Str("a"), Str("b"), Str("c")),
            Def("custom.input_a", BlockShape.Reporter),
            Def("custom.input_b", BlockShape.Reporter),
            Def("custom.input_c", BlockShape.Reporter),
            Def("motion.move_forward", BlockShape.Statement, 0, Num("distance"), Num("duration")),
            Def("variables.change", BlockShape.Statement, 0, Str("name"), Num("by"), Choices("scope", "everyone", "this_object")),
            Def("variables.set", BlockShape.Statement, 0, Str("name"), Str("value"), Choices("scope", "everyone", "this_object")),
            Def("operator.subtract", BlockShape.Reporter, 0, Num("a"), Num("b")),
            Def("operator.add", BlockShape.Reporter, 0, Num("a"), Num("b")),
            Def("operator.gt", BlockShape.Boolean, 0, Str("a"), Str("b")),
            Def("control.if", BlockShape.CBlock, 1, Rep("condition")),
            Def("control.repeat", BlockShape.CBlock, 1, Num("times")),
            Def("control.wait", BlockShape.Statement, 0, Num("seconds")),
            Def("control.stop", BlockShape.Statement, 0, Choices("target", "this_script", "all", "other_scripts"))
        });

        private static int _nextId;
        private static string Id() => "c" + ++_nextId;

        private static BlockNode Node(string blockType, BlockParam[] parameters = null, params BlockNode[][] branches) =>
            new() { id = Id(), blockType = blockType, parameters = parameters ?? new BlockParam[0], branches = branches };

        private static BlockParam Number(string key, float value) => new() { key = key, kind = ParamKind.Number, number = value };
        private static BlockParam Text(string key, string value) => new() { key = key, kind = ParamKind.Text, text = value };
        private static BlockParam Choice(string key, string id) => new() { key = key, kind = ParamKind.Choice, text = id };
        private static BlockParam Slot(string key, ParamKind kind, BlockNode block) => new() { key = key, kind = kind, reporter = block };

        private static BlockNode Move(float distance) => Node("motion.move_forward", new[] { Number("distance", distance), Number("duration", 0f) });
        private static BlockNode MoveBy(BlockNode distance) => Node("motion.move_forward", new[] { Slot("distance", ParamKind.Number, distance), Number("duration", 0f) });
        private static BlockNode Input(char which) => Node("custom.input_" + which);
        private static BlockNode Count(string variable) => Node("variables.change", new[] { Text("name", variable), Number("by", 1f), Choice("scope", "everyone") });
        private static BlockNode Wait(float seconds) => Node("control.wait", new[] { Number("seconds", seconds) });

        private static BlockNode Run(string name, params BlockParam[] inputs)
        {
            var parameters = new BlockParam[4];
            parameters[0] = Text("name", name);
            parameters[1] = Text("a", "");
            parameters[2] = Text("b", "");
            parameters[3] = Text("c", "");
            foreach (var input in inputs) parameters[input.key[0] - 'a' + 1] = input;
            return Node(CustomBlocks.RunType, parameters);
        }

        private static BlockParam A(float value) => Text("a", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        private static BlockParam A(BlockNode value) => Slot("a", ParamKind.Text, value);

        private static BlockStack Main(params BlockNode[] sequence) =>
            new() { id = "stk_main", triggerBlockType = "event.when_play_clicked", sequence = sequence };

        private static BlockStack Define(string name, params BlockNode[] sequence) => new()
        {
            id = "stk_" + Id(),
            triggerBlockType = CustomBlocks.DefineType,
            triggerParameters = new[] { Text("name", name) },
            sequence = sequence
        };

        private VmScheduler _scheduler;

        /// <summary>Compiles the program, starts its first stack on the target, and hands back that thread.</summary>
        private VmThread Start(params BlockStack[] stacks)
        {
            var registry = BuildRegistry();
            var result = ProgramCompiler.Link(new ObjectProgram { stacks = stacks }, registry);
            CollectionAssert.IsEmpty(result.Diagnostics, "the test program should compile cleanly");

            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, typeof(OpTableBuilder).Assembly);
            _scheduler = new VmScheduler(steps, conditions, values);
            return _scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[0]);
        }

        private float Z => _target.transform.position.z;
        private static float Variable(string name) => BlockyRuntime.Variables.Get(name).AsNumber();

        // ---- running a custom block ------------------------------------------------------------------

        [Test]
        public void RunningACustomBlock_RunsItsScript_ThenCarriesOnAfterIt()
        {
            var thread = Start(Main(Run("jump"), Move(1f)), Define("jump", Move(10f)));

            _scheduler.Tick(1f);

            Assert.AreEqual(11f, Z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
            Assert.AreEqual(0, thread.CallDepth);
        }

        [Test]
        public void TheDefinition_CanBeAnywhereInTheProgram_BeforeOrAfterTheScriptThatRunsIt()
        {
            // A definition compiled before the calling script sits below its entry pc, one compiled after sits past
            // its exit: both were outside the script's own range, which is why the thread's bound moves (ADR-029).
            var registry = BuildRegistry();
            var result = ProgramCompiler.Link(new ObjectProgram { stacks = new[] { Define("jump", Move(10f)), Main(Run("jump"), Move(1f)) } }, registry);
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, typeof(OpTableBuilder).Assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            var thread = scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[1]);

            scheduler.Tick(1f);

            Assert.AreEqual(11f, Z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void EachRunHandsItsOwnInputs_ToTheDefinition()
        {
            Start(Main(Run("step", A(3f)), Run("step", A(4f))), Define("step", MoveBy(Input('a'))));

            _scheduler.Tick(1f);

            Assert.AreEqual(7f, Z, 1e-4f);
        }

        [Test]
        public void AllThreeInputs_ArriveWhereTheyWereSent()
        {
            var set = Node("variables.set", new[] { Text("name", "joined"), Slot("value", ParamKind.Text, Input('c')), Choice("scope", "everyone") });
            Start(Main(Run("go", Text("a", "1"), Text("b", "2"), Text("c", "3"))),
                Define("go", MoveBy(Input('b')), set));

            _scheduler.Tick(1f);

            Assert.AreEqual(2f, Z, 1e-4f);
            Assert.AreEqual(3f, Variable("joined"), 1e-4f);
        }

        [Test]
        public void TheInputsAreWorkedOut_BeforeTheCall_InTheCallersOwnContext()
        {
            // outer passes ((input a) + 1) on to inner, so inner's input a is outer's plus one
            var plusOne = Node("operator.add", new[] { Slot("a", ParamKind.Number, Input('a')), Number("b", 1f) });
            Start(Main(Run("outer", A(2f))),
                Define("outer", Run("inner", A(plusOne))),
                Define("inner", MoveBy(Input('a'))));

            _scheduler.Tick(1f);

            Assert.AreEqual(3f, Z, 1e-4f);
        }

        [Test]
        public void ANameNoDefinitionHas_DoesNothing()
        {
            var thread = Start(Main(Run("nothing like this"), Move(1f)), Define("jump", Move(10f)));

            _scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void NamesIgnoreCaseAndSurroundingSpace()
        {
            Start(Main(Run("  JUMP ")), Define("Jump", Move(10f)));

            _scheduler.Tick(1f);

            Assert.AreEqual(10f, Z, 1e-4f);
        }

        [Test]
        public void WhenTwoDefinitionsShareAName_TheFirstOneRuns()
        {
            Start(Main(Run("jump")), Define("jump", Move(10f)), Define("JUMP", Move(100f)));

            _scheduler.Tick(1f);

            Assert.AreEqual(10f, Z, 1e-4f);
        }

        [Test]
        public void AnInputBlock_OutsideAnyDefinition_ReadsAsEmpty()
        {
            var set = Node("variables.set", new[] { Text("name", "v"), Slot("value", ParamKind.Text, Input('a')), Choice("scope", "everyone") });
            Start(Main(set));

            _scheduler.Tick(1f);

            Assert.AreEqual(string.Empty, BlockyRuntime.Variables.Get("v").AsText());
        }

        [Test]
        public void LoopsInsideADefinition_InsideALoop_KeepTheirOwnCounts()
        {
            var repeatTwice = Node("control.repeat", new[] { Number("times", 2f) }, new[] { Move(1f) });
            var repeatThrice = Node("control.repeat", new[] { Number("times", 3f) }, new[] { Run("twice") });
            var thread = Start(Main(repeatThrice), Define("twice", repeatTwice));

            for (var i = 0; i < 20 && thread.State != ThreadState.Done; i++) _scheduler.Tick(1f);

            Assert.AreEqual(6f, Z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void ATimedBlockInsideADefinition_HoldsTheCallerUntilItIsDone()
        {
            var thread = Start(Main(Run("slow"), Move(1f)), Define("slow", Wait(1f), Move(10f)));

            _scheduler.Tick(0.5f);
            Assert.AreEqual(0f, Z, 1e-4f, "still waiting inside the custom block");
            Assert.AreEqual(1, thread.CallDepth);

            _scheduler.Tick(1f);

            Assert.AreEqual(11f, Z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void StopThisScript_InsideADefinition_EndsTheWholeScript()
        {
            var stop = Node("control.stop", new[] { Choice("target", "this_script") });
            var thread = Start(Main(Run("quit"), Move(1f)), Define("quit", stop));

            _scheduler.Tick(1f);

            Assert.AreEqual(0f, Z, 1e-4f, "as in Scratch, stop ends the script that ran the block, not just the block");
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        // ---- recursion -------------------------------------------------------------------------------

        [Test]
        public void ACustomBlockCanRunItself_AndEachLevelKeepsItsOwnInput()
        {
            // define countdown: if (input a) > 0 { change calls by 1; run countdown ((input a) - 1) }
            var minusOne = Node("operator.subtract", new[] { Slot("a", ParamKind.Number, Input('a')), Number("b", 1f) });
            var positive = Node("operator.gt", new[] { Slot("a", ParamKind.Text, Input('a')), Text("b", "0") });
            var body = Node("control.if", new[] { Slot("condition", ParamKind.Reporter, positive) },
                new[] { Count("calls"), Run("countdown", A(minusOne)) });
            var thread = Start(Main(Run("countdown", A(5f)), Count("after")), Define("countdown", body));

            _scheduler.Tick(1f);
            Assert.AreEqual(1f, Variable("calls"), 1e-4f, "a block running itself waits a frame first, as in Scratch");

            for (var i = 0; i < 10 && thread.State != ThreadState.Done; i++) _scheduler.Tick(1f);

            Assert.AreEqual(5f, Variable("calls"), 1e-4f);
            Assert.AreEqual(1f, Variable("after"), 1e-4f, "every level returned, and the script carried on after the first run");
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void RecursionThatNeverStops_EndsAtTheNestingLimit_InsteadOfTheGame()
        {
            var thread = Start(Main(Run("forever"), Count("after")), Define("forever", Count("depth"), Run("forever")));

            for (var i = 0; i < VmThread.MaxNestingDepth + 10 && thread.State != ThreadState.Done; i++) _scheduler.Tick(1f);

            Assert.AreEqual(VmThread.MaxNestingDepth, Variable("depth"), 1e-4f);
            Assert.AreEqual(1f, Variable("after"), 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        // ---- step back -------------------------------------------------------------------------------

        [Test]
        public void StepBack_RestoresBeingInsideACustomBlock_WithItsInputs()
        {
            var thread = Start(Main(Run("slow", A(5f))), Define("slow", Wait(1f), MoveBy(Input('a'))));
            _scheduler.Tick(0.5f);
            var moment = _scheduler.SaveMoment();

            _scheduler.Tick(1f);
            Assert.AreEqual(ThreadState.Done, thread.State);
            Assert.AreEqual(0, thread.CallDepth);

            _scheduler.RestoreMoment(moment);

            Assert.AreEqual(1, thread.CallDepth);
            Assert.AreEqual(5f, thread.Input(0).AsNumber(), 1e-4f);
            Assert.AreNotEqual(ThreadState.Done, thread.State);

            _scheduler.Resume();
            _scheduler.Tick(1f);
            Assert.AreEqual(10f, Z, 1e-4f, "it ran the rest of the custom block again, with the same input");
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        // ---- the runner ------------------------------------------------------------------------------

        [Test]
        public void ADefinitionNeverStartsOnItsOwn_NotOnPlay_NotOnStep()
        {
            var registry = BuildRegistry();
            var broker = new TriggerBroker();
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, typeof(OpTableBuilder).Assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            BlockyRuntime.SetForTests(registry, scheduler, broker);

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(new ObjectProgram { stacks = new[] { Main(Move(1f)), Define("jump", Move(10f)) } });
            _target.SetActive(false);
            var runner = _target.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            runner.Initialize();

            broker.FirePlayClicked();
            scheduler.Tick(1f);
            Assert.AreEqual(1f, Z, 1e-4f, "only the play script ran");

            broker.FireStepAll();
            scheduler.Tick(1f);
            Assert.AreEqual(2f, Z, 1e-4f, "Step started the play script again, and still not the definition");

            Object.DestroyImmediate(asset);
        }
    }
}
