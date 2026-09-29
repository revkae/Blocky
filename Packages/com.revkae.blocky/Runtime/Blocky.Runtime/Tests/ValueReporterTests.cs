using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>
    /// Value reporters: the round blocks, and the fact that any white input can hold one. Most of these run a real
    /// program and read the object's z, because that is the only thing a block can actually do with a number —
    /// <c>move forward (…)</c> is the assertion.
    /// </summary>
    public class ValueReporterTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("BlockyValueTarget");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_target);

        // ---- BlockValue itself ----------------------------------------------------------------------

        [Test]
        public void TextThatLooksLikeANumber_ReadsAsThatNumber_AndTextThatDoesNotReadsAsZero()
        {
            Assert.AreEqual(12.5f, BlockValue.Text("12.5").AsNumber(), 1e-4f);
            Assert.AreEqual(0f, BlockValue.Text("apple").AsNumber(), 1e-4f);
            Assert.AreEqual(0f, BlockValue.Empty.AsNumber(), 1e-4f);
        }

        [Test]
        public void NumbersPrintWithoutATrailingZero_AndAlwaysWithADot()
        {
            Assert.AreEqual("3", BlockValue.Number(3f).AsText());
            Assert.AreEqual("1.5", BlockValue.Number(1.5f).AsText());
        }

        [Test]
        public void EmptyTextAndZeroAreFalse_EverythingElseIsTrue()
        {
            Assert.IsFalse(BlockValue.Empty.AsBool());
            Assert.IsFalse(BlockValue.Number(0f).AsBool());
            Assert.IsFalse(BlockValue.Text("false").AsBool());
            Assert.IsTrue(BlockValue.Number(-1f).AsBool());
            Assert.IsTrue(BlockValue.Text("anything").AsBool());
        }

        // ---- fixtures -------------------------------------------------------------------------------

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
            Def("motion.move_forward", BlockShape.Statement, parameters: new[] { Num("distance"), Num("duration") }),
            Def("control.if", BlockShape.CBlock, 1, new[] { Rep("condition") }),
            Def("operator.add", BlockShape.Reporter, parameters: new[] { Num("a"), Num("b") }),
            Def("operator.subtract", BlockShape.Reporter, parameters: new[] { Num("a"), Num("b") }),
            Def("operator.multiply", BlockShape.Reporter, parameters: new[] { Num("a"), Num("b") }),
            Def("operator.divide", BlockShape.Reporter, parameters: new[] { Num("a"), Num("b") }),
            Def("operator.mod", BlockShape.Reporter, parameters: new[] { Num("a"), Num("b") }),
            Def("operator.round", BlockShape.Reporter, parameters: new[] { Num("n") }),
            Def("operator.math", BlockShape.Reporter,
                parameters: new[] { Choices("op", "abs", "floor", "ceiling", "sqrt", "sin", "cos", "tan", "ln", "log", "e_pow", "ten_pow"), Num("n") }),
            Def("operator.random", BlockShape.Reporter, parameters: new[] { Num("from"), Num("to") }),
            Def("operator.join", BlockShape.Reporter, parameters: new[] { Str("a"), Str("b") }),
            Def("operator.letter_of", BlockShape.Reporter, parameters: new[] { Num("index"), Str("text") }),
            Def("operator.length", BlockShape.Reporter, parameters: new[] { Str("text") }),
            Def("operator.lt", BlockShape.Boolean, parameters: new[] { Str("a"), Str("b") }),
            Def("operator.eq", BlockShape.Boolean, parameters: new[] { Str("a"), Str("b") }),
            Def("operator.gt", BlockShape.Boolean, parameters: new[] { Str("a"), Str("b") }),
            Def("operator.contains", BlockShape.Boolean, parameters: new[] { Str("text"), Str("thing") }),
            Def("sensing.timer", BlockShape.Reporter),
            Def("motion.position", BlockShape.Reporter, parameters: new[] { Choices("axis", "x", "y", "z") }),
            Def("condition.true", BlockShape.Boolean),
            Def("condition.false", BlockShape.Boolean)
        });

        private static int _nextId;
        private static string Id() => "n" + ++_nextId;

        private static BlockNode Node(string blockType, params BlockParam[] parameters) =>
            new() { id = Id(), blockType = blockType, parameters = parameters ?? new BlockParam[0] };

        private static BlockParam Number(string key, float value) => new() { key = key, kind = ParamKind.Number, number = value };
        private static BlockParam Text(string key, string value) => new() { key = key, kind = ParamKind.Text, text = value };
        private static BlockParam Choice(string key, string id) => new() { key = key, kind = ParamKind.Choice, text = id };

        /// <summary>An input filled with a block instead of a typed-in value — what dropping a reporter on an oval produces.</summary>
        private static BlockParam Slot(string key, ParamKind kind, BlockNode block) => new() { key = key, kind = kind, reporter = block };

        private static BlockParam NumberSlot(string key, BlockNode block) => Slot(key, ParamKind.Number, block);
        private static BlockParam TextSlot(string key, BlockNode block) => Slot(key, ParamKind.Text, block);
        private static BlockParam ConditionSlot(string key, BlockNode block) => Slot(key, ParamKind.Reporter, block);

        private (VmScheduler scheduler, CompileResult result) Compile(params BlockNode[] sequence)
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = sequence } }
            };
            var result = ProgramCompiler.Link(program, registry);

            var assembly = typeof(OpTableBuilder).Assembly;
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, assembly);
            return (new VmScheduler(steps, conditions, values), result);
        }

        /// <summary>
        /// Runs <c>move forward</c> with <paramref name="distance"/> in its distance input and answers how far it
        /// went. The object goes back to the origin first, so several calls in one test each measure their own move
        /// rather than the running total.
        /// </summary>
        private float MoveBy(BlockNode distance, Vector3? startAt = null)
        {
            var move = Node("motion.move_forward", NumberSlot("distance", distance), Number("duration", 0f));
            var (scheduler, result) = Compile(move);
            CollectionAssert.IsEmpty(result.Diagnostics);
            _target.transform.position = startAt ?? Vector3.zero;
            scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[0]);
            scheduler.Tick(1f);
            return _target.transform.position.z - (startAt ?? Vector3.zero).z;
        }

        /// <summary>Runs <c>if &lt;condition&gt; { move forward 1 }</c> and answers whether the body ran.</summary>
        private bool ConditionHolds(BlockNode condition)
        {
            var body = Node("motion.move_forward", Number("distance", 1f), Number("duration", 0f));
            var iff = new BlockNode
            {
                id = Id(),
                blockType = "control.if",
                parameters = new[] { ConditionSlot("condition", condition) },
                branches = new[] { new[] { body } }
            };

            var (scheduler, result) = Compile(iff);
            CollectionAssert.IsEmpty(result.Diagnostics);
            _target.transform.position = Vector3.zero; // each call answers for its own condition, not the total so far
            scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[0]);
            scheduler.Tick(1f);
            return _target.transform.position.z > 0.5f;
        }

        // ---- reporters in ordinary inputs ------------------------------------------------------------

        [Test]
        public void AReporterInANumberInput_IsWhatTheBlockUses()
        {
            Assert.AreEqual(5f, MoveBy(Node("operator.add", Number("a", 2f), Number("b", 3f))), 1e-4f);
        }

        [Test]
        public void ReportersNest_SoAnInputCanHoldAWholeExpression()
        {
            // (2 x 3) + 4
            var product = Node("operator.multiply", Number("a", 2f), Number("b", 3f));
            var sum = Node("operator.add", NumberSlot("a", product), Number("b", 4f));

            Assert.AreEqual(10f, MoveBy(sum), 1e-4f);
        }

        [Test]
        public void AnEmptyInputReadsAsZero()
        {
            Assert.AreEqual(0f, MoveBy(Node("operator.add", NumberSlot("a", null), Number("b", 0f))), 1e-4f);
        }

        [TestCase(7f, 4f, 3f)]
        [TestCase(-1f, 4f, 3f)] // the sign follows the divisor, as in Scratch — this is what makes mod wrap a range
        [TestCase(5f, 0f, 0f)]
        public void Mod_TakesTheSignOfItsDivisor(float a, float b, float expected)
        {
            Assert.AreEqual(expected, MoveBy(Node("operator.mod", Number("a", a), Number("b", b))), 1e-4f);
        }

        [Test]
        public void DividingByZero_AnswersZero_RatherThanSendingTheObjectToInfinity()
        {
            Assert.AreEqual(0f, MoveBy(Node("operator.divide", Number("a", 5f), Number("b", 0f))), 1e-4f);
        }

        [Test]
        public void MathOf_ReadsItsOperationFromTheChoice()
        {
            Assert.AreEqual(3f, MoveBy(Node("operator.math", Choice("op", "sqrt"), Number("n", 9f))), 1e-4f);
            Assert.AreEqual(4f, MoveBy(Node("operator.math", Choice("op", "abs"), Number("n", -4f))), 1e-4f);
            Assert.AreEqual(2f, MoveBy(Node("operator.math", Choice("op", "floor"), Number("n", 2.7f))), 1e-4f);
        }

        [Test]
        public void PickRandom_StaysInsideItsRange_AndAnswersAWholeNumberForWholeEnds()
        {
            for (var i = 0; i < 25; i++)
            {
                var value = MoveBy(Node("operator.random", Number("from", 1f), Number("to", 6f)));
                _target.transform.position = Vector3.zero;
                Assert.GreaterOrEqual(value, 1f);
                Assert.LessOrEqual(value, 6f);
                Assert.AreEqual(value, Mathf.Round(value), 1e-4f);
            }
        }

        [Test]
        public void JoinAndLength_WorkOnText_AndTheirResultReadsBackAsANumber()
        {
            var join = Node("operator.join", Text("a", "ab"), Text("b", "cde"));
            var length = Node("operator.length", TextSlot("text", join));

            Assert.AreEqual(5f, MoveBy(length), 1e-4f);
        }

        [Test]
        public void LetterOf_CountsFromOne_AndIsEmptyOutsideTheText()
        {
            Assert.IsTrue(ConditionHolds(Node("operator.eq",
                TextSlot("a", Node("operator.letter_of", Number("index", 2f), Text("text", "world"))), Text("b", "o"))));

            Assert.IsTrue(ConditionHolds(Node("operator.eq",
                TextSlot("a", Node("operator.letter_of", Number("index", 99f), Text("text", "world"))), Text("b", ""))));
        }

        // ---- comparisons -----------------------------------------------------------------------------

        [TestCase("10", "9", true)]   // both look like numbers, so 10 wins — as words, "10" sorts before "9"
        [TestCase("apple", "banana", false)]
        public void GreaterThan_ComparesNumbersAsNumbersAndWordsAsWords(string a, string b, bool expected)
        {
            Assert.AreEqual(expected, ConditionHolds(Node("operator.gt", Text("a", a), Text("b", b))));
        }

        [Test]
        public void Equals_IgnoresCase_AndReadsNumbersWrittenDifferently()
        {
            Assert.IsTrue(ConditionHolds(Node("operator.eq", Text("a", "APPLE"), Text("b", "apple"))));
            Assert.IsTrue(ConditionHolds(Node("operator.eq", Text("a", "10"), Text("b", "10.0"))));
        }

        [Test]
        public void Contains_IsCaseInsensitive()
        {
            Assert.IsTrue(ConditionHolds(Node("operator.contains", Text("text", "Apple"), Text("thing", "PPL"))));
            Assert.IsFalse(ConditionHolds(Node("operator.contains", Text("text", "Apple"), Text("thing", "z"))));
        }

        [Test]
        public void AReporterInAComparison_IsEvaluatedBeforeTheComparison()
        {
            var sum = Node("operator.add", Number("a", 4f), Number("b", 3f));
            Assert.IsTrue(ConditionHolds(Node("operator.gt", TextSlot("a", sum), Text("b", "5"))));
        }

        // ---- the two shapes mixing -------------------------------------------------------------------

        [Test]
        public void AConditionDroppedIntoAValueInput_ReadsAsTrueOrFalse()
        {
            var joined = Node("operator.join", TextSlot("a", Node("condition.true")), Text("b", "!"));
            Assert.IsTrue(ConditionHolds(Node("operator.eq", TextSlot("a", joined), Text("b", "true!"))));
        }

        // A reporter in a *hexagonal* hole is refused at compile time instead — see
        // AReporterInAHexagonalHole_IsAnError. SlotEvaluator still coerces one if a program somehow holds it, so a
        // hand-edited file degrades to "not zero is true" rather than throwing mid-frame.

        // ---- reporters that read the world -----------------------------------------------------------

        [Test]
        public void Timer_IsAvailableAsAValue_NotJustAsAQuestion()
        {
            var move = Node("motion.move_forward", NumberSlot("distance", Node("sensing.timer")), Number("duration", 0f));
            var (scheduler, result) = Compile(move);
            CollectionAssert.IsEmpty(result.Diagnostics);
            scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[0]);

            scheduler.Tick(2f);

            Assert.AreEqual(2f, _target.transform.position.z, 1e-3f, "the clock had advanced 2s when the block ran");
        }

        [Test]
        public void Position_ReadsTheAxisItsChoiceNames()
        {
            // move forward (position y), starting at y = 7 → it moves 7 along z
            Assert.AreEqual(7f, MoveBy(Node("motion.position", Choice("axis", "y")), new Vector3(3f, 7f, 0f)), 1e-4f);
        }

        // ---- compiling -------------------------------------------------------------------------------

        [Test]
        public void AReporterInANumberInput_SkipsTheRangeCheckOnTheTypedInValue()
        {
            // The literal is ignored once a block fills the input, so its range cannot be the thing that fails.
            var huge = Node("operator.add", Number("a", 999999f), Number("b", 999999f));
            var move = new BlockNode
            {
                id = Id(),
                blockType = "motion.move_forward",
                parameters = new[] { NumberSlot("distance", huge), Number("duration", 0f) }
            };

            var (_, result) = Compile(move);

            CollectionAssert.IsEmpty(result.Diagnostics);
        }

        [Test]
        public void AStatementBlockInAnInput_IsAnError()
        {
            var move = new BlockNode
            {
                id = Id(),
                blockType = "motion.move_forward",
                parameters = new[]
                {
                    NumberSlot("distance", Node("motion.move_forward", Number("distance", 1f), Number("duration", 0f))),
                    Number("duration", 0f)
                }
            };

            var (_, result) = Compile(move);

            Assert.IsTrue(result.HasErrors);
            StringAssert.Contains("is not a reporter", result.Diagnostics[0].Message);
        }

        [Test]
        public void AReporterInAHexagonalHole_IsAnError()
        {
            var iff = new BlockNode
            {
                id = Id(),
                blockType = "control.if",
                parameters = new[] { ConditionSlot("condition", Node("operator.add", Number("a", 1f), Number("b", 1f))) },
                branches = new[] { new BlockNode[0] }
            };

            var (_, result) = Compile(iff);

            Assert.IsTrue(result.HasErrors);
            StringAssert.Contains("is not a condition", result.Diagnostics[0].Message);
        }
    }
}
