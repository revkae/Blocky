using System.Collections.Generic;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Compiler.Tests
{
    /// <summary>Custom blocks at compile time (ADR-029): which definitions a <c>run</c> block can reach, and the advice.</summary>
    public class CustomBlockCompileTests
    {
        private static BlockDefinition Def(string blockType, BlockShape shape, params ParamSpec[] parameters)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.shape = shape;
            def.parameters = parameters;
            return def;
        }

        private static ParamSpec Str(string key) => new() { key = key, kind = ParamKind.Text };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_go_clicked", BlockShape.Trigger),
            Def(CustomBlocks.DefineType, BlockShape.Trigger, Str("name")),
            Def(CustomBlocks.RunType, BlockShape.Statement, Str("name"), Str("a"), Str("b"), Str("c")),
            Def("custom.input_a", BlockShape.Reporter),
            Def("motion.move_forward", BlockShape.Statement, new ParamSpec { key = "distance", kind = ParamKind.Number, min = -100, max = 100 }),
            Def("operator.join", BlockShape.Reporter, Str("a"), Str("b")),
            Def("looks.say", BlockShape.Statement, Str("message"))
        });

        private static BlockParam Text(string key, string value) => new() { key = key, kind = ParamKind.Text, text = value };

        private static BlockNode Move(string id) => new()
        {
            id = id,
            blockType = "motion.move_forward",
            parameters = new[] { new BlockParam { key = "distance", kind = ParamKind.Number, number = 1f } }
        };

        private static BlockNode Run(string id, string name) => new()
        {
            id = id,
            blockType = CustomBlocks.RunType,
            parameters = new[] { Text("name", name), Text("a", ""), Text("b", ""), Text("c", "") }
        };

        private static BlockNode SayInput(string id) => new()
        {
            id = id,
            blockType = "looks.say",
            parameters = new[]
            {
                new BlockParam
                {
                    key = "message", kind = ParamKind.Text,
                    reporter = new BlockNode
                    {
                        id = id + "_join", blockType = "operator.join",
                        parameters = new[]
                        {
                            Text("a", "hi "),
                            new BlockParam { key = "b", kind = ParamKind.Text, reporter = new BlockNode { id = id + "_input", blockType = "custom.input_a" } }
                        }
                    }
                }
            }
        };

        private static BlockStack Go(string id, params BlockNode[] sequence) =>
            new() { id = id, triggerBlockType = "event.when_go_clicked", sequence = sequence };

        private static BlockStack Define(string id, string name, params BlockNode[] sequence) => new()
        {
            id = id,
            triggerBlockType = CustomBlocks.DefineType,
            triggerParameters = new[] { Text("name", name) },
            sequence = sequence
        };

        private static ObjectProgram Program(params BlockStack[] stacks) => new() { stacks = stacks };

        // ---- the procedure table ---------------------------------------------------------------------

        [Test]
        public void ADefinition_IsFoundByItsName_TrimmedAndIgnoringCase()
        {
            var compiled = ProgramCompiler.Link(Program(Go("stk_go", Run("n_run", "jump")), Define("stk_def", " Jump ", Move("n_move"))), BuildRegistry()).Program;

            Assert.IsTrue(compiled.TryFindProcedure("JUMP  ", out var entry, out var exit));
            Assert.AreEqual(compiled.StackEntryPoints[1], entry);
            Assert.AreEqual(compiled.StackExitPoints[1], exit);
            Assert.IsFalse(compiled.TryFindProcedure("fly", out _, out _));
        }

        [Test]
        public void OfTwoDefinitionsWithOneName_OnlyTheFirstCanBeReached()
        {
            var compiled = ProgramCompiler.Link(Program(Define("stk_1", "jump", Move("n1")), Define("stk_2", "JUMP", Move("n2"))), BuildRegistry()).Program;

            Assert.IsTrue(compiled.TryFindProcedure("jump", out var entry, out _));
            Assert.AreEqual(compiled.StackEntryPoints[0], entry);
            Assert.IsNull(compiled.ProcedureNames[1]);
        }

        [Test]
        public void TheFirstDefinitionOfAName_OwnsIt_EvenWithNothingToRun()
        {
            // The advice tells the learner the second one never runs, so it must not quietly stand in.
            var compiled = ProgramCompiler.Link(Program(Define("stk_1", "jump"), Define("stk_2", "jump", Move("n2"))), BuildRegistry()).Program;

            Assert.IsFalse(compiled.TryFindProcedure("jump", out _, out _));
        }

        [Test]
        public void AnEmptyOrNamelessDefinition_IsNotRunnable()
        {
            var compiled = ProgramCompiler.Link(Program(Define("stk_1", "empty"), Define("stk_2", "  ", Move("n2"))), BuildRegistry()).Program;

            Assert.IsFalse(compiled.TryFindProcedure("empty", out _, out _), "no blocks, nothing to run");
            Assert.IsFalse(compiled.TryFindProcedure("", out _, out _));
        }

        [Test]
        public void AScriptWithNoBlocks_GetsNoEntryPoint()
        {
            var compiled = ProgramCompiler.Link(Program(Go("stk_empty"), Go("stk_go", Move("n1"))), BuildRegistry()).Program;

            Assert.AreEqual(-1, compiled.StackEntryPoints[0]);
            Assert.AreEqual(0, compiled.StackEntryPoints[1]);
            Assert.AreEqual(1, compiled.ExitPcFor(0));
        }

        [Test]
        public void DefinedNames_ListsEachNamedDefinitionOnce_InOrder()
        {
            var names = CustomBlocks.DefinedNames(Program(
                Define("stk_1", "jump", Move("n1")), Go("stk_go"), Define("stk_2", ""), Define("stk_3", "spin"), Define("stk_4", "JUMP ")));

            CollectionAssert.AreEqual(new[] { "jump", "spin" }, names);
        }

        // ---- advice ----------------------------------------------------------------------------------

        private static List<Advice> Advise(params BlockStack[] stacks) => ProgramAdvice.Collect(Program(stacks), BuildRegistry());

        [Test]
        public void AWorkingCustomBlock_GetsNoAdvice()
        {
            var advice = Advise(Go("stk_go", Run("n_run", "JUMP")), Define("stk_def", "jump", Move("n_move"), SayInput("n_say")));

            CollectionAssert.IsEmpty(advice);
        }

        [Test]
        public void ARunWithANameNothingDefines_IsTold()
        {
            var advice = Advise(Go("stk_go", Run("n_run", "jupm")), Define("stk_def", "jump", Move("n_move")));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual(AdviceKind.Hint, advice[0].Kind);
            Assert.AreEqual("n_run", advice[0].NodeId);
            StringAssert.Contains("“jupm”", advice[0].Message);
            StringAssert.Contains("nothing happens", advice[0].Message);
        }

        [Test]
        public void ARunWithNoName_IsToldToTypeOne()
        {
            var advice = Advise(Go("stk_go", Run("n_run", "  ")), Define("stk_def", "jump", Move("n_move")));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual("n_run", advice[0].NodeId);
            StringAssert.Contains("Type the name", advice[0].Message);
        }

        [Test]
        public void ADefinitionWithNoName_IsToldToGetOne()
        {
            var advice = Advise(Define("stk_def", "", Move("n_move")));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual("stk_def", advice[0].StackId);
            Assert.IsNull(advice[0].NodeId, "pinned to the define block itself");
            StringAssert.Contains("a name", advice[0].Message);
        }

        [Test]
        public void TheSecondDefinitionOfAName_IsToldItNeverRuns()
        {
            var advice = Advise(Define("stk_1", "jump", Move("n1")), Define("stk_2", "Jump", Move("n2")));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual("stk_2", advice[0].StackId);
            StringAssert.Contains("never runs", advice[0].Message);
        }

        [Test]
        public void AnInputBlockOutsideADefinition_IsTold_OnTheBlockThatHoldsIt()
        {
            var advice = Advise(Go("stk_go", SayInput("n_say")));

            Assert.AreEqual(1, advice.Count);
            Assert.AreEqual("n_say", advice[0].NodeId, "the input sits two blocks deep; the table outlines the statement");
            StringAssert.Contains("always empty", advice[0].Message);
        }
    }
}
