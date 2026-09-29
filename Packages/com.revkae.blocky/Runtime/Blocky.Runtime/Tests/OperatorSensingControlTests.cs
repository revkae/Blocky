using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>
    /// The blocks added for Scratch/Delightex parity that need no value reporters: the boolean operators, the
    /// sensing conditions, <c>wait until</c>, <c>stop</c>, and the two new motion blocks.
    /// </summary>
    public class OperatorSensingControlTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("BlockyCatalogTarget");

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            BlockyInput.SetForTests(null);
        }

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

        private static ParamSpec Num(string key) => new() { key = key, kind = ParamKind.Number, min = -1000, max = 1000 };
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
            Def("motion.change_position", BlockShape.Statement,
                parameters: new[] { Num("x"), Num("y"), Num("z"), Num("duration"), Choices("space", "world", "local") }),
            Def("motion.glide_to", BlockShape.Statement, parameters: new[] { Num("x"), Num("y"), Num("z"), Num("duration") }),
            Def("control.if", BlockShape.CBlock, 1, new[] { Rep("condition") }),
            Def("control.repeat_until", BlockShape.CBlock, 1, new[] { Rep("condition") }),
            Def("control.wait_until", BlockShape.Statement, parameters: new[] { Rep("condition") }),
            Def("control.stop", BlockShape.Statement, parameters: new[] { Choices("target", "this_script", "all", "other_scripts") }),
            Def("sensing.reset_timer", BlockShape.Statement),
            Def("sensing.timer_above", BlockShape.Boolean, parameters: new[] { Num("seconds") }),
            Def("sensing.touching", BlockShape.Boolean, parameters: new[] { new ParamSpec { key = "tag", kind = ParamKind.Text } }),
            Def("operator.and", BlockShape.Boolean, parameters: new[] { Rep("a"), Rep("b") }),
            Def("operator.or", BlockShape.Boolean, parameters: new[] { Rep("a"), Rep("b") }),
            Def("operator.not", BlockShape.Boolean, parameters: new[] { Rep("a") }),
            Def("condition.true", BlockShape.Boolean),
            Def("condition.false", BlockShape.Boolean),
            Def("condition.mouse_down", BlockShape.Boolean)
        });

        private static int _nextId;
        private static string NewId() => "n" + ++_nextId;

        private static BlockNode Node(string blockType, BlockParam[] parameters = null, BlockNode[][] branches = null) => new()
        {
            id = NewId(),
            blockType = blockType,
            parameters = parameters ?? new BlockParam[0],
            branches = branches ?? new BlockNode[0][]
        };

        private static BlockParam Number(string key, float value) => new() { key = key, kind = ParamKind.Number, number = value };
        private static BlockParam Choice(string key, string id) => new() { key = key, kind = ParamKind.Choice, text = id };
        private static BlockParam Text(string key, string value) => new() { key = key, kind = ParamKind.Text, text = value };
        private static BlockParam Slot(string key, BlockNode condition) => new() { key = key, kind = ParamKind.Reporter, reporter = condition };

        /// <summary>A bare condition, e.g. Cond("condition.true") or Cond("operator.not", Slot("a", ...)).</summary>
        private static BlockNode Cond(string blockType, params BlockParam[] parameters) => Node(blockType, parameters);

        private static BlockNode Move(float distance = 1f) => Node("motion.move_forward", new[] { Number("distance", distance), Number("duration", 0f) });

        private (VmScheduler scheduler, CompiledProgram program) Compile(params BlockNode[][] stacks)
        {
            var registry = BuildRegistry();
            var blockStacks = new BlockStack[stacks.Length];
            for (var i = 0; i < stacks.Length; i++)
                blockStacks[i] = new BlockStack { id = "stk_" + i, triggerBlockType = "event.when_play_clicked", sequence = stacks[i] };

            var result = ProgramCompiler.Link(new ObjectProgram { stacks = blockStacks }, registry);
            CollectionAssert.IsEmpty(result.Diagnostics, "the test program should compile cleanly");

            var assembly = typeof(OpTableBuilder).Assembly;
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, assembly), OpTableBuilder.BuildConditions(registry, assembly));
            return (scheduler, result.Program);
        }

        /// <summary>Compiles one stack, starts it, and hands back the scheduler and its thread.</summary>
        private (VmScheduler scheduler, VmThread thread) Start(params BlockNode[] sequence)
        {
            var (scheduler, program) = Compile(sequence);
            return (scheduler, scheduler.Start(program, _target, program.StackEntryPoints[0]));
        }

        private float Z => _target.transform.position.z;

        // ---- Operators ------------------------------------------------------------------------------

        [TestCase("condition.true", 0f)]
        [TestCase("condition.false", 1f)]
        public void Not_InvertsWhateverIsInItsSlot(string inner, float expectedZ)
        {
            var (scheduler, _) = Start(Node("control.if",
                new[] { Slot("condition", Cond("operator.not", Slot("a", Cond(inner)))) },
                new[] { new[] { Move() } }));

            scheduler.Tick(1f);

            Assert.AreEqual(expectedZ, Z, 1e-4f);
        }

        [TestCase("condition.true", "condition.true", 1f)]
        [TestCase("condition.true", "condition.false", 0f)]
        [TestCase("condition.false", "condition.true", 0f)]
        [TestCase("condition.false", "condition.false", 0f)]
        public void And_IsTrue_OnlyWhenBothSlotsAre(string a, string b, float expectedZ)
        {
            var (scheduler, _) = Start(Node("control.if",
                new[] { Slot("condition", Cond("operator.and", Slot("a", Cond(a)), Slot("b", Cond(b)))) },
                new[] { new[] { Move() } }));

            scheduler.Tick(1f);

            Assert.AreEqual(expectedZ, Z, 1e-4f);
        }

        [TestCase("condition.true", "condition.false", 1f)]
        [TestCase("condition.false", "condition.true", 1f)]
        [TestCase("condition.false", "condition.false", 0f)]
        public void Or_IsTrue_WhenEitherSlotIs(string a, string b, float expectedZ)
        {
            var (scheduler, _) = Start(Node("control.if",
                new[] { Slot("condition", Cond("operator.or", Slot("a", Cond(a)), Slot("b", Cond(b)))) },
                new[] { new[] { Move() } }));

            scheduler.Tick(1f);

            Assert.AreEqual(expectedZ, Z, 1e-4f);
        }

        [Test]
        public void Operators_Nest_SoAnAndCanHoldAnotherAnd()
        {
            var inner = Cond("operator.and", Slot("a", Cond("condition.true")), Slot("b", Cond("condition.true")));
            var outer = Cond("operator.and", Slot("a", Cond("condition.true")), Slot("b", inner));

            var (scheduler, _) = Start(Node("control.if", new[] { Slot("condition", outer) }, new[] { new[] { Move() } }));
            scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f, "every level of the nest is evaluated, not just the outermost one");
        }

        [Test]
        public void EmptySlotInAnOperator_ReadsAsFalse_LikeAnEmptyHexagon()
        {
            var (scheduler, _) = Start(Node("control.if",
                new[] { Slot("condition", Cond("operator.and", Slot("a", Cond("condition.true")), Slot("b", null))) },
                new[] { new[] { Move() } }));

            scheduler.Tick(1f);

            Assert.AreEqual(0f, Z, 1e-4f);
        }

        // ---- wait until / stop ----------------------------------------------------------------------

        [Test]
        public void WaitUntil_HoldsTheScript_UntilItsConditionBecomesTrue()
        {
            var held = false;
            BlockyInput.SetForTests(() => held);

            var (scheduler, _) = Start(
                Node("control.wait_until", new[] { Slot("condition", Cond("condition.mouse_down")) }),
                Move());

            scheduler.Tick(1f);
            Assert.AreEqual(0f, Z, 1e-4f, "the block after the wait must not run yet");

            held = true;
            scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f);
        }

        [Test]
        public void Stop_ThisScript_EndsTheThread_SoTheBlockAfterItNeverRuns()
        {
            var (scheduler, thread) = Start(Node("control.stop", new[] { Choice("target", "this_script") }), Move());

            scheduler.Tick(1f);

            Assert.AreEqual(0f, Z, 1e-4f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void Stop_All_EndsEveryOtherScriptToo()
        {
            var (scheduler, program) = Compile(
                new[] { Node("control.stop", new[] { Choice("target", "all") }) },
                new[] { Move(), Move() });

            scheduler.Start(program, _target, program.StackEntryPoints[0]);
            var other = scheduler.Start(program, _target, program.StackEntryPoints[1]);

            scheduler.Tick(1f);

            Assert.AreEqual(0f, Z, 1e-4f, "the second script never got to move");
            Assert.AreEqual(ThreadState.Done, other.State);
        }

        [Test]
        public void Stop_OtherScripts_EndsTheOthers_ButCarriesOnItself()
        {
            var (scheduler, program) = Compile(
                new[] { Node("control.stop", new[] { Choice("target", "other_scripts") }), Move() },
                new[] { Move() });

            scheduler.Start(program, _target, program.StackEntryPoints[0]);
            var other = scheduler.Start(program, _target, program.StackEntryPoints[1]);

            scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f, "only the stopping script's own move ran");
            Assert.AreEqual(ThreadState.Done, other.State);
        }

        [Test]
        public void AScriptThatRunsOutOfBlocks_EndsThere_InsteadOfFallingIntoTheNextScript()
        {
            // Every stack of a program is emitted into one instruction array, one after another (ADR-019).
            var (scheduler, program) = Compile(new[] { Move() }, new[] { Move(), Move() });
            var first = scheduler.Start(program, _target, program.StackEntryPoints[0]);

            scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f, "only its own single move ran");
            Assert.AreEqual(ThreadState.Done, first.State);
        }

        // ---- timer ----------------------------------------------------------------------------------

        [Test]
        public void TimerAbove_IsFalse_UntilTheScriptClockPassesIt()
        {
            var (scheduler, thread) = Start(Node("control.repeat_until",
                new[] { Slot("condition", Cond("sensing.timer_above", new[] { Number("seconds", 1.5f) })) },
                new[] { new[] { Move() } }));

            scheduler.Tick(1f);
            Assert.AreEqual(1f, Z, 1e-4f, "one lap at t=1s, which is not past 1.5s yet");

            scheduler.Tick(1f);

            Assert.AreEqual(1f, Z, 1e-4f, "the loop ended at t=2s instead of running another lap");
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void ResetTimer_PutsTheTimerBackToZero_WithoutTouchingTheScriptClock()
        {
            var (scheduler, program) = Compile(new[] { Node("sensing.reset_timer") });

            scheduler.Tick(1f);
            Assert.AreEqual(1f, scheduler.Timer, 1e-4f);

            scheduler.Start(program, _target, program.StackEntryPoints[0]);
            scheduler.Tick(0f);

            Assert.AreEqual(0f, scheduler.Timer, 1e-4f);
            Assert.AreEqual(1f, scheduler.Now, 1e-4f, "the clock itself keeps running");
        }

        // ---- new motion blocks ----------------------------------------------------------------------

        [Test]
        public void ChangePosition_WithNoDuration_MovesByTheOffsetAtOnce()
        {
            var (scheduler, _) = Start(Node("motion.change_position", new[]
            {
                Number("x", 2f), Number("y", 3f), Number("z", 4f), Number("duration", 0f), Choice("space", "world")
            }));

            scheduler.Tick(1f);

            Assert.AreEqual(new Vector3(2f, 3f, 4f), _target.transform.position);
        }

        [Test]
        public void ChangePosition_OverTime_ArrivesAfterItsDuration()
        {
            var (scheduler, thread) = Start(Node("motion.change_position", new[]
            {
                Number("x", 0f), Number("y", 0f), Number("z", 4f), Number("duration", 2f), Choice("space", "world")
            }));

            scheduler.Tick(1f);
            Assert.AreEqual(2f, Z, 1e-3f, "half the offset after half the duration");

            scheduler.Tick(1f);

            Assert.AreEqual(4f, Z, 1e-3f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void GlideTo_LandsExactlyOnItsTarget()
        {
            _target.transform.position = new Vector3(10f, 0f, 0f);
            var (scheduler, thread) = Start(Node("motion.glide_to", new[]
            {
                Number("x", 0f), Number("y", 0f), Number("z", 6f), Number("duration", 3f)
            }));

            scheduler.Tick(1f);
            Assert.AreNotEqual(6f, Z, "still on its way");

            scheduler.Tick(1f);
            scheduler.Tick(1f);

            Assert.AreEqual(new Vector3(0f, 0f, 6f), _target.transform.position);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        // ---- touching -------------------------------------------------------------------------------

        [Test]
        public void Touching_IsFalse_ForAnObjectThatHasReportedNoContacts()
        {
            Assert.IsFalse(BlockCollisionRelay.IsTouching(_target, string.Empty), "no relay, so nothing has ever touched it");

            var (scheduler, _) = Start(Node("control.if",
                new[] { Slot("condition", Cond("sensing.touching", new[] { Text("tag", "Player") })) },
                new[] { new[] { Move() } }));

            scheduler.Tick(1f);

            Assert.AreEqual(0f, Z, 1e-4f);
        }
    }
}
