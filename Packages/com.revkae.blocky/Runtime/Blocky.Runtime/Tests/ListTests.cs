using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>Lists (ADR-028): the store beside the variables, and the nine blocks that use it.</summary>
    public class ListTests
    {
        private GameObject _target;
        private GameObject _other;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("BlockyListTarget");
            _other = new GameObject("BlockyListOther");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            Object.DestroyImmediate(_other);
            BlockyRuntime.Reset(); // the store lives on BlockyRuntime; each test starts from nothing
        }

        private static BlockyVariables ListOf(params string[] items)
        {
            var variables = new BlockyVariables();
            foreach (var item in items) variables.Add("items", BlockValue.Text(item));
            return variables;
        }

        // ---- the store -------------------------------------------------------------------------------

        [Test]
        public void AListThatWasNeverUsed_IsEmpty_AndReadingItDoesNotMakeIt()
        {
            var variables = new BlockyVariables();

            Assert.AreEqual(0, variables.Length("items"));
            Assert.AreEqual(string.Empty, variables.Item("items", 1f).AsText());
            Assert.AreEqual(0, variables.PositionOf("items", BlockValue.Text("a")));
            Assert.IsFalse(variables.Contains("items", BlockValue.Text("a")));
            Assert.AreEqual(0, variables.SharedLists.Count, "reading a list must not put it on the watcher");
        }

        [Test]
        public void AddPutsItemsAtTheEnd_AndPositionsCountFromOne()
        {
            var variables = ListOf("a", "b", "c");

            Assert.AreEqual(3, variables.Length("items"));
            Assert.AreEqual("a", variables.Item("items", 1f).AsText());
            Assert.AreEqual("c", variables.Item("items", 3f).AsText());
        }

        [Test]
        public void PositionsAreRoundedDown_AndOnesTheListDoesNotHave_ReadAsEmpty()
        {
            var variables = ListOf("a", "b", "c");

            Assert.AreEqual("b", variables.Item("items", 2.7f).AsText());
            Assert.AreEqual("c", variables.Item("items", 3.5f).AsText());
            Assert.AreEqual(string.Empty, variables.Item("items", 0.5f).AsText());
            Assert.AreEqual(string.Empty, variables.Item("items", 0f).AsText());
            Assert.AreEqual(string.Empty, variables.Item("items", -1f).AsText());
            Assert.AreEqual(string.Empty, variables.Item("items", 4f).AsText());
            Assert.AreEqual(string.Empty, variables.Item("items", float.NaN).AsText());
        }

        [Test]
        public void ChangingAPositionTheListDoesNotHave_DoesNothing()
        {
            var variables = ListOf("a", "b");

            variables.DeleteAt("items", 0f);
            variables.DeleteAt("items", 3f);
            variables.Replace("items", 5f, BlockValue.Text("z"));
            variables.Insert("items", 4f, BlockValue.Text("z")); // one past the end is 3 here, not 4

            Assert.AreEqual(2, variables.Length("items"));
            Assert.AreEqual("a", variables.Item("items", 1f).AsText());
            Assert.AreEqual("b", variables.Item("items", 2f).AsText());
        }

        [Test]
        public void DeleteMovesTheRestUp_AndReplaceKeepsTheLength()
        {
            var variables = ListOf("a", "b", "c");

            variables.DeleteAt("items", 1f);
            variables.Replace("items", 2f, BlockValue.Text("z"));

            Assert.AreEqual(2, variables.Length("items"));
            Assert.AreEqual("b", variables.Item("items", 1f).AsText());
            Assert.AreEqual("z", variables.Item("items", 2f).AsText());
        }

        [Test]
        public void InsertMovesTheRestAlong_AndOnePastTheEndAddsToTheEnd()
        {
            var variables = new BlockyVariables();

            variables.Insert("items", 1f, BlockValue.Text("b")); // into an empty list: 1 is one past the end
            variables.Insert("items", 1f, BlockValue.Text("a"));
            variables.Insert("items", 3f, BlockValue.Text("c"));

            Assert.AreEqual(3, variables.Length("items"));
            Assert.AreEqual("a", variables.Item("items", 1f).AsText());
            Assert.AreEqual("b", variables.Item("items", 2f).AsText());
            Assert.AreEqual("c", variables.Item("items", 3f).AsText());
        }

        [Test]
        public void DeleteAll_EmptiesTheList_WhichStillExists()
        {
            var variables = ListOf("a", "b");

            variables.DeleteAll("items");
            variables.DeleteAll("fresh"); // the usual first block of a program that fills a list

            Assert.AreEqual(0, variables.Length("items"));
            Assert.IsTrue(variables.SharedLists.ContainsKey("items"));
            Assert.IsTrue(variables.SharedLists.ContainsKey("fresh"), "an emptied list shows on the watcher as []");
        }

        [Test]
        public void FindingAnItem_MatchesTheWayEqualsDoes()
        {
            var variables = new BlockyVariables();
            variables.Add("items", BlockValue.Text("Apple"));
            variables.Add("items", BlockValue.Text("10.0"));
            variables.Add("items", BlockValue.Text("apple"));

            Assert.AreEqual(1, variables.PositionOf("items", BlockValue.Text("APPLE")), "words ignore case, and the first match wins");
            Assert.AreEqual(2, variables.PositionOf("items", BlockValue.Number(10f)), "10 finds \"10.0\"");
            Assert.AreEqual(0, variables.PositionOf("items", BlockValue.Text("pear")));
            Assert.IsTrue(variables.Contains("items", BlockValue.Text("10")));
            Assert.IsFalse(variables.Contains("items", BlockValue.Text("pear")));
        }

        [Test]
        public void ListNamesIgnoreCaseAndSurroundingSpace_AndAnEmptyNameIsNoList()
        {
            var variables = new BlockyVariables();

            variables.Add("High Scores", BlockValue.Number(5f));
            variables.Add(" high scores ", BlockValue.Number(7f));
            variables.Add("  ", BlockValue.Number(1f));

            Assert.AreEqual(2, variables.Length("HIGH SCORES"));
            Assert.AreEqual(1, variables.SharedLists.Count);
        }

        [Test]
        public void EachObjectHasItsOwnCopy_OfAPerObjectList_AndNeitherIsTheSharedOne()
        {
            var variables = new BlockyVariables();

            variables.Add("path", BlockValue.Text("left"), _target);
            variables.Add("path", BlockValue.Text("right"), _other);
            variables.Add("path", BlockValue.Text("up"), _other);
            variables.Add("path", BlockValue.Text("shared"));

            Assert.AreEqual(1, variables.Length("path", _target));
            Assert.AreEqual(2, variables.Length("path", _other));
            Assert.AreEqual("shared", variables.Item("path", 1f).AsText());
            Assert.AreEqual(1, variables.SharedLists.Count, "only the shared list is on the watcher");
        }

        [Test]
        public void AListAndAVariable_WithTheSameName_AreDifferentThings()
        {
            var variables = new BlockyVariables();

            variables.Set("score", BlockValue.Number(3f));
            variables.Add("score", BlockValue.Number(9f));

            Assert.AreEqual(3f, variables.Get("score").AsNumber(), 1e-4f);
            Assert.AreEqual(9f, variables.Item("score", 1f).AsNumber(), 1e-4f);
        }

        [Test]
        public void ANumberAddedToAList_IsStillANumberWhenItComesBack()
        {
            var variables = new BlockyVariables();

            variables.Add("items", BlockValue.Number(4f));

            Assert.AreEqual(BlockValue.ValueKind.Number, variables.Item("items", 1f).Kind);
        }

        [Test]
        public void AListStopsGrowing_AtTheCap()
        {
            var variables = new BlockyVariables();

            for (var i = 0; i < BlockyVariables.MaxListLength + 5; i++) variables.Add("items", BlockValue.Number(i));
            variables.Insert("items", 1f, BlockValue.Text("one too many"));

            Assert.AreEqual(BlockyVariables.MaxListLength, variables.Length("items"));
            Assert.AreEqual(0f, variables.Item("items", 1f).AsNumber(), 1e-4f, "a refused insert moves nothing");
        }

        [Test]
        public void Clear_ForgetsListsToo()
        {
            var variables = ListOf("a");
            variables.Add("mine", BlockValue.Text("b"), _target);

            variables.Clear();

            Assert.AreEqual(0, variables.Length("items"));
            Assert.AreEqual(0, variables.Length("mine", _target));
            Assert.AreEqual(0, variables.SharedLists.Count);
        }

        // ---- the blocks ------------------------------------------------------------------------------

        private static BlockDefinition Def(string blockType, BlockShape shape, params ParamSpec[] parameters)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.category = BlockCategory.Lists;
            def.parameters = parameters;
            return def;
        }

        private static ParamSpec Position() => new() { key = "index", kind = ParamKind.Number, defaultNumber = 1, min = 1, max = 10000 };
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
            Def("variables.set", BlockShape.Statement, Str("name"), Str("value"), Scope()),
            Def("lists.add", BlockShape.Statement, Str("item"), Str("list"), Scope()),
            Def("lists.delete", BlockShape.Statement, Position(), Str("list"), Scope()),
            Def("lists.delete_all", BlockShape.Statement, Str("list"), Scope()),
            Def("lists.insert", BlockShape.Statement, Str("item"), Position(), Str("list"), Scope()),
            Def("lists.replace", BlockShape.Statement, Position(), Str("list"), Str("item"), Scope()),
            Def("lists.item", BlockShape.Reporter, Position(), Str("list"), Scope()),
            Def("lists.index_of", BlockShape.Reporter, Str("item"), Str("list"), Scope()),
            Def("lists.length", BlockShape.Reporter, Str("list"), Scope()),
            Def("lists.contains", BlockShape.Boolean, Str("list"), Str("item"), Scope())
        });

        private static int _nextId;
        private static string Id() => "l" + ++_nextId;

        private static BlockNode Node(string blockType, params BlockParam[] parameters) =>
            new() { id = Id(), blockType = blockType, parameters = parameters };

        private static BlockParam Number(string key, float value) => new() { key = key, kind = ParamKind.Number, number = value };
        private static BlockParam Text(string key, string value) => new() { key = key, kind = ParamKind.Text, text = value };
        private static BlockParam Choice(string key, string id) => new() { key = key, kind = ParamKind.Choice, text = id };
        private static BlockParam Slot(string key, BlockNode block) => new() { key = key, kind = ParamKind.Text, reporter = block };

        private static BlockParam Everyone() => Choice("scope", "everyone");

        private static BlockNode SetVariable(string name, BlockNode to) =>
            Node("variables.set", Text("name", name), Slot("value", to), Everyone());

        private static BlockNode Add(string item, string scope = "everyone") =>
            Node("lists.add", Text("item", item), Text("list", "items"), Choice("scope", scope));

        private void Run(GameObject on, params BlockNode[] sequence)
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = sequence } }
            };
            var result = ProgramCompiler.Link(program, registry);
            CollectionAssert.IsEmpty(result.Diagnostics);

            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, typeof(OpTableBuilder).Assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            scheduler.Start(result.Program, on, result.Program.StackEntryPoints[0]);
            scheduler.Tick(1f);
        }

        [Test]
        public void TheStatementBlocks_BuildAList()
        {
            Run(_target,
                Node("lists.delete_all", Text("list", "items"), Everyone()),
                Add("a"),
                Add("b"),
                Node("lists.insert", Text("item", "c"), Number("index", 1f), Text("list", "items"), Everyone()),   // c a b
                Node("lists.replace", Number("index", 2f), Text("list", "items"), Text("item", "z"), Everyone()),  // c z b
                Node("lists.delete", Number("index", 3f), Text("list", "items"), Everyone()));                     // c z

            var lists = BlockyRuntime.Variables;
            Assert.AreEqual(2, lists.Length("items"));
            Assert.AreEqual("c", lists.Item("items", 1f).AsText());
            Assert.AreEqual("z", lists.Item("items", 2f).AsText());
        }

        [Test]
        public void TheReportersAndTheCondition_ReadTheList()
        {
            Run(_target,
                Add("apple"),
                Add("pear"),
                SetVariable("count", Node("lists.length", Text("list", "items"), Everyone())),
                SetVariable("second", Node("lists.item", Number("index", 2f), Text("list", "items"), Everyone())),
                SetVariable("where", Node("lists.index_of", Text("item", "PEAR"), Text("list", "items"), Everyone())),
                SetVariable("has", Node("lists.contains", Text("list", "items"), Text("item", "apple"), Everyone())),
                SetVariable("lacks", Node("lists.contains", Text("list", "items"), Text("item", "plum"), Everyone())));

            var variables = BlockyRuntime.Variables;
            Assert.AreEqual(2f, variables.Get("count").AsNumber(), 1e-4f);
            Assert.AreEqual("pear", variables.Get("second").AsText());
            Assert.AreEqual(2f, variables.Get("where").AsNumber(), 1e-4f);
            Assert.IsTrue(variables.Get("has").AsBool());
            Assert.IsFalse(variables.Get("lacks").AsBool());
        }

        [Test]
        public void APerObjectList_IsSeparateForEachObjectRunningTheSameProgram()
        {
            Run(_target, Add("mine", "this_object"));
            Run(_other, Add("theirs", "this_object"), Add("also theirs", "this_object"));

            var variables = BlockyRuntime.Variables;
            Assert.AreEqual(1, variables.Length("items", _target));
            Assert.AreEqual(2, variables.Length("items", _other));
            Assert.AreEqual(0, variables.Length("items"), "and none of it touched the shared one");
        }

        [Test]
        public void AnAddedItemCanComeFromAnotherBlock_AndKeepsWhatItIs()
        {
            Run(_target,
                Add("x"),
                Node("lists.add", Slot("item", Node("lists.length", Text("list", "items"), Everyone())), Text("list", "items"), Everyone()));

            Assert.AreEqual(BlockValue.ValueKind.Number, BlockyRuntime.Variables.Item("items", 2f).Kind);
            Assert.AreEqual(1f, BlockyRuntime.Variables.Item("items", 2f).AsNumber(), 1e-4f);
        }
    }
}
