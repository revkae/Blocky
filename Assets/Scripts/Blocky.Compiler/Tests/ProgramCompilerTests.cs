using System.Linq;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Compiler.Tests
{
    public class ProgramCompilerTests
    {
        private static BlockDefinition Def(string blockType, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static BlockRegistry BuildCatalogRegistry()
        {
            return BlockRegistry.Build(new[]
            {
                Def("event.when_play_clicked"),
                Def("motion.move_forward", parameters: new[]
                {
                    new ParamSpec { key = "distance", kind = ParamKind.Number, min = 0, max = 100 }
                }),
                Def("control.repeat", branchCount: 1, parameters: new[]
                {
                    new ParamSpec { key = "times", kind = ParamKind.Number, min = 0, max = 1000 }
                }),
                Def("control.if_else", branchCount: 2)
            });
        }

        private static ObjectProgram SampleProgram()
        {
            return new ObjectProgram
            {
                targetObjectUid = "obj_1",
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk_1",
                        triggerBlockType = "event.when_play_clicked",
                        sequence = new[]
                        {
                            new BlockNode
                            {
                                id = "n_repeat",
                                blockType = "control.repeat",
                                parameters = new[] { new BlockParam { key = "times", kind = ParamKind.Number, number = 4 } },
                                branches = new[]
                                {
                                    new[]
                                    {
                                        new BlockNode
                                        {
                                            id = "n_move",
                                            blockType = "motion.move_forward",
                                            parameters = new[] { new BlockParam { key = "distance", kind = ParamKind.Number, number = 1 } }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        [Test]
        public void Validate_ValidProgram_HasNoErrors()
        {
            var diagnostics = ProgramCompiler.Validate(SampleProgram(), BuildCatalogRegistry());
            Assert.IsFalse(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
        }

        [Test]
        public void Validate_UnknownBlockType_ReportsError()
        {
            var program = SampleProgram();
            program.stacks[0].sequence[0].blockType = "does.not_exist";

            var diagnostics = ProgramCompiler.Validate(program, BuildCatalogRegistry());
            Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("Unknown block type")));
        }

        [Test]
        public void Validate_DuplicateId_ReportsError()
        {
            var program = SampleProgram();
            program.stacks[0].sequence[0].branches[0][0].id = "n_repeat"; // collides with parent

            var diagnostics = ProgramCompiler.Validate(program, BuildCatalogRegistry());
            Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("Duplicate id")));
        }

        [Test]
        public void Validate_MissingRequiredParam_ReportsError()
        {
            var program = SampleProgram();
            program.stacks[0].sequence[0].parameters = new BlockParam[0];

            var diagnostics = ProgramCompiler.Validate(program, BuildCatalogRegistry());
            Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("Missing required param")));
        }

        [Test]
        public void Validate_OutOfRangeNumber_ReportsError()
        {
            var program = SampleProgram();
            program.stacks[0].sequence[0].parameters[0].number = 9999;

            var diagnostics = ProgramCompiler.Validate(program, BuildCatalogRegistry());
            Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("out of range")));
        }

        [Test]
        public void Validate_WrongBranchCount_ReportsError()
        {
            var program = SampleProgram();
            program.stacks[0].sequence[0].branches = new BlockNode[0][];

            var diagnostics = ProgramCompiler.Validate(program, BuildCatalogRegistry());
            Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("expects 1 branch")));
        }

        [Test]
        public void Link_ValidProgram_ProducesEntryPointAndBranchBounds()
        {
            var result = ProgramCompiler.Link(SampleProgram(), BuildCatalogRegistry());

            Assert.IsFalse(result.HasErrors);
            Assert.AreEqual(0, result.Program.StackEntryPoints[0]);

            var repeatInstr = result.Program.Code[0];
            Assert.AreEqual(1, repeatInstr.JumpA);       // branch body starts right after the repeat instruction
            Assert.AreEqual(2, repeatInstr.JumpAExit);   // one instruction (move_forward) in the branch
            Assert.AreEqual(-1, repeatInstr.JumpB);

            var moveInstr = result.Program.Code[1];
            Assert.AreEqual(1f, result.Program.ParamTable[moveInstr.ParamOffset].Number);
        }

        [Test]
        public void Link_LooseStack_IsNotCompiled_AndIsNotAnError()
        {
            var program = SampleProgram();
            var loose = new BlockStack
            {
                id = "stk_loose",
                triggerBlockType = "",
                sequence = new[]
                {
                    new BlockNode
                    {
                        id = "n_loose",
                        blockType = "motion.move_forward",
                        parameters = new[] { new BlockParam { key = "distance", kind = ParamKind.Number, number = 1 } }
                    }
                }
            };
            program.stacks = new[] { program.stacks[0], loose };

            var result = ProgramCompiler.Link(program, BuildCatalogRegistry());

            Assert.IsFalse(result.HasErrors);
            Assert.AreEqual(0, result.Program.StackEntryPoints[0]);
            Assert.AreEqual(-1, result.Program.StackEntryPoints[1]);
        }

        [Test]
        public void Link_StackWithError_IsSkippedButOthersStillCompile()
        {
            var program = SampleProgram();
            var brokenStack = new BlockStack { id = "stk_broken", triggerBlockType = "does.not_exist" };
            program.stacks = new[] { program.stacks[0], brokenStack };

            var result = ProgramCompiler.Link(program, BuildCatalogRegistry());

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(0, result.Program.StackEntryPoints[0]); // good stack still compiled
            Assert.AreEqual(-1, result.Program.StackEntryPoints[1]); // broken stack skipped
        }
    }
}
