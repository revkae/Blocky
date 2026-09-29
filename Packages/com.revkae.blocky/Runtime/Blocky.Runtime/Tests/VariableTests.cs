using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>Variables: the store itself, and the three blocks that use it.</summary>
    public class VariableTests
    {
        private GameObject _target;
        private GameObject _other;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("BlockyVariableTarget");
            _other = new GameObject("BlockyVariableOther");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            Object.DestroyImmediate(_other);
            BlockyRuntime.Reset(); // the store lives on BlockyRuntime; each test starts from nothing
        }

        // ---- the store -------------------------------------------------------------------------------

        [Test]
        public void AVariableThatWasNeverSet_ReadsAsEmpty_WhichIsZeroAsANumber()
        {
            var variables = new BlockyVariables();

            Assert.AreEqual(0f, variables.Get("score").AsNumber(), 1e-4f);
            Assert.AreEqual(string.Empty, variables.Get("score").AsText());
        }

        [Test]
        public void ChangingAVariableThatWasNeverSet_CountsFromZero()
        {
            var variables = new BlockyVariables();

            variables.Change("score", 5f);

            Assert.AreEqual(5f, variables.Get("score").AsNumber(), 1e-4f);
        }

        [Test]
        public void NamesIgnoreCaseAndSurroundingSpace()
        {
            var variables = new BlockyVariables();

            variables.Set("Score", BlockValue.Number(3f));

            Assert.AreEqual(3f, variables.Get(" score ").AsNumber(), 1e-4f);
        }

        [Test]
        public void EachObjectHasItsOwnCopy_OfAPerObjectVariable_AndNeitherIsTheSharedOne()
        {
            var variables = new BlockyVariables();

            variables.Set("lives", BlockValue.Number(1f), _target);
            variables.Set("lives", BlockValue.Number(2f), _other);
            variables.Set("lives", BlockValue.Number(9f));

            Assert.AreEqual(1f, variables.Get("lives", _target).AsNumber(), 1e-4f);
            Assert.AreEqual(2f, variables.Get("lives", _other).AsNumber(), 1e-4f);
            Assert.AreEqual(9f, variables.Get("lives").AsNumber(), 1e-4f);
        }

        [Test]
        public void SettingANumber_KeepsItANumber_SoComparisonsStillWork()
        {
            var variables = new BlockyVariables();

            variables.Set("score", BlockValue.Number(10f));

            Assert.AreEqual(BlockValue.ValueKind.Number, variables.Get("score").Kind);
        }

        // ---- the blocks ------------------------------------------------------------------------------

        private static BlockDefinition Def(string blockType, BlockShape shape, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static ParamSpec Num(string key) => new() { key = key, kind = ParamKind.Number, min = -1000000, max = 1000000 };
        private static ParamSpec Str(string key) => new() { key = key, kind = ParamKind.Text };

        private static ParamSpec Scope() => new()
        {
            key = "scope",
            kind = ParamKind.Choice,
            defaultText = "everyone",
            choices = new[]
            {
                new ChoiceEntry { stableId = "everyone", displayNameKey = "everyone" },
                new ChoiceEntry { stableId = "this_object", displayNameKey = "this object" }
            }
        };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("motion.move_forward", BlockShape.Statement, parameters: new[] { Num("distance"), Num("duration") }),
            Def("variables.set", BlockShape.Statement, parameters: new[] { Str("name"), Str("value"), Scope() }),
            Def("variables.change", BlockShape.Statement, parameters: new[] { Str("name"), Num("by"), Scope() }),
            Def("variables.get", BlockShape.Reporter, parameters: new[] { Str("name"), Scope() }),
            Def("operator.add", BlockShape.Reporter, parameters: new[] { Num("a"), Num("b") })
        });

        private static int _nextId;
        private static string Id() => "v" + ++_nextId;

        private static BlockNode Node(string blockType, params BlockParam[] parameters) =>
            new() { id = Id(), blockType = blockType, parameters = parameters };

        private static BlockParam Number(string key, float value) => new() { key = key, kind = ParamKind.Number, number = value };
        private static BlockParam Text(string key, string value) => new() { key = key, kind = ParamKind.Text, text = value };
        private static BlockParam Choice(string key, string id) => new() { key = key, kind = ParamKind.Choice, text = id };
        private static BlockParam Slot(string key, ParamKind kind, BlockNode block) => new() { key = key, kind = kind, reporter = block };

        private static BlockNode Get(string name, string scope = "everyone") =>
            Node("variables.get", Text("name", name), Choice("scope", scope));

        /// <summary>Runs one sequence on <paramref name="on"/> and hands back the scheduler that ran it.</summary>
        private VmScheduler Run(GameObject on, params BlockNode[] sequence)
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = sequence } }
            };
            var result = ProgramCompiler.Link(program, registry);
            CollectionAssert.IsEmpty(result.Diagnostics);

            var assembly = typeof(OpTableBuilder).Assembly;
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            scheduler.Start(result.Program, on, result.Program.StackEntryPoints[0]);
            scheduler.Tick(1f);
            return scheduler;
        }

        [Test]
        public void SetThenChange_LeavesTheVariableWithTheSum()
        {
            Run(_target,
                Node("variables.set", Text("name", "score"), Text("value", "10"), Choice("scope", "everyone")),
                Node("variables.change", Text("name", "score"), Number("by", 5f), Choice("scope", "everyone")));

            Assert.AreEqual(15f, BlockyRuntime.Variables.Get("score").AsNumber(), 1e-4f);
        }

        [Test]
        public void AVariableReadsBackIntoAnyInput()
        {
            Run(_target,
                Node("variables.set", Text("name", "steps"), Text("value", "4"), Choice("scope", "everyone")),
                Node("motion.move_forward", Slot("distance", ParamKind.Number, Get("steps")), Number("duration", 0f)));

            Assert.AreEqual(4f, _target.transform.position.z, 1e-4f);
        }

        [Test]
        public void AVariableCanBeSetFromAnExpressionThatReadsItself()
        {
            // set score to ((value of score) + (2)), twice — the classic accumulator
            var plusTwo = Node("operator.add", Slot("a", ParamKind.Number, Get("score")), Number("b", 2f));
            var set = Node("variables.set", Text("name", "score"), Slot("value", ParamKind.Text, plusTwo), Choice("scope", "everyone"));

            var plusTwoAgain = Node("operator.add", Slot("a", ParamKind.Number, Get("score")), Number("b", 2f));
            var setAgain = Node("variables.set", Text("name", "score"), Slot("value", ParamKind.Text, plusTwoAgain), Choice("scope", "everyone"));

            Run(_target, set, setAgain);

            Assert.AreEqual(4f, BlockyRuntime.Variables.Get("score").AsNumber(), 1e-4f);
        }

        [Test]
        public void APerObjectVariable_IsSeparateForEachObjectRunningTheSameProgram()
        {
            var countMine = Node("variables.change", Text("name", "hits"), Number("by", 1f), Choice("scope", "this_object"));

            Run(_target, countMine);
            Run(_other, countMine);
            Run(_other, countMine);

            Assert.AreEqual(1f, BlockyRuntime.Variables.Get("hits", _target).AsNumber(), 1e-4f);
            Assert.AreEqual(2f, BlockyRuntime.Variables.Get("hits", _other).AsNumber(), 1e-4f);
            Assert.AreEqual(0f, BlockyRuntime.Variables.Get("hits").AsNumber(), 1e-4f, "and none of it touched the shared one");
        }

        [Test]
        public void ReadingAPerObjectVariable_ReadsTheRunningObjectsCopy()
        {
            BlockyRuntime.Variables.Set("lift", BlockValue.Number(3f), _target);
            BlockyRuntime.Variables.Set("lift", BlockValue.Number(7f), _other);

            var move = Node("motion.move_forward",
                Slot("distance", ParamKind.Number, Get("lift", "this_object")), Number("duration", 0f));

            Run(_target, move);
            Run(_other, move);

            Assert.AreEqual(3f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(7f, _other.transform.position.z, 1e-4f);
        }
    }
}
