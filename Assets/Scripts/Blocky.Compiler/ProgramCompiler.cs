using System;
using System.Collections.Generic;
using System.Linq;
using Blocky.Data;
using Unity.Profiling;

namespace Blocky.Compiler
{
    /// <summary>
    /// Where every string lookup, validation, and parameter resolution happens — once, at load time
    /// (TDD §3, §6.2). <see cref="Link"/> compiles every stack that has no error diagnostic and skips the
    /// rest (StackEntryPoints[i] = -1) rather than failing the whole program, per TDD §10.2.
    /// Condition blocks (<see cref="BlockShape.Boolean"/>) live inside <see cref="ParamKind.Reporter"/> params,
    /// never in a sequence; they compile to a <see cref="ParamValue.Reporter"/> slot the VM evaluates on demand.
    /// </summary>
    public static class ProgramCompiler
    {
        private static readonly ProfilerMarker LinkMarker = new("Blocky.ProgramCompiler.Link");

        public static IReadOnlyList<CompileDiagnostic> Validate(ObjectProgram program, BlockRegistry registry)
        {
            var diagnostics = new List<CompileDiagnostic>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var stack in program.stacks)
            {
                if (!seenIds.Add(stack.id))
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stack.id, null, $"Duplicate id '{stack.id}'."));

                // A loose stack (no hat) is blocks lying on the table: legal, just never run. Checked first because
                // registry.Find(null) would throw.
                var loose = ProgramQuery.IsLoose(stack);
                if (!loose && registry.Find(stack.triggerBlockType) == null)
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stack.id, null,
                        $"Unknown block type '{stack.triggerBlockType}'."));

                ValidateSequence(stack.id, loose, stack.sequence, registry, seenIds, diagnostics);
            }

            return diagnostics;
        }

        private static void ValidateSequence(string stackId, bool loose, BlockNode[] sequence, BlockRegistry registry,
            HashSet<string> seenIds, List<CompileDiagnostic> diagnostics)
        {
            foreach (var node in sequence)
            {
                if (!seenIds.Add(node.id))
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, node.id, $"Duplicate id '{node.id}'."));

                var def = registry.Find(node.blockType);
                if (def == null)
                {
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, node.id,
                        $"Unknown block type '{node.blockType}'."));
                    continue; // nothing to check params/branches against
                }

                // A condition lying loose on the table is fine (it just never runs); inside a script it has no step to perform.
                if (def.shape == BlockShape.Boolean && !loose)
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, node.id,
                        $"'{node.blockType}' is a condition — it only fits in a condition slot, not in a sequence."));

                if (node.branches.Length != def.branchCount)
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, node.id,
                        $"'{node.blockType}' expects {def.branchCount} branch(es), found {node.branches.Length}."));

                ValidateParams(stackId, node, def, registry, seenIds, diagnostics);

                foreach (var branch in node.branches)
                    ValidateSequence(stackId, loose, branch, registry, seenIds, diagnostics);
            }
        }

        private static void ValidateParams(string stackId, BlockNode node, BlockDefinition def, BlockRegistry registry,
            HashSet<string> seenIds, List<CompileDiagnostic> diagnostics)
        {
            foreach (var spec in def.parameters)
            {
                var param = Array.Find(node.parameters, p => p.key == spec.key);
                if (param == null)
                {
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, node.id,
                        $"Missing required param '{spec.key}'."));
                    continue;
                }

                if (spec.kind == ParamKind.Number && (param.number < spec.min || param.number > spec.max))
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, node.id,
                        $"Param '{spec.key}' = {param.number} is out of range [{spec.min}, {spec.max}]."));

                if (spec.kind == ParamKind.Choice && Array.FindIndex(spec.choices, c => c.stableId == param.text) < 0)
                    diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, node.id,
                        $"'{param.text}' is not a valid choice for param '{spec.key}'."));

                if (spec.kind == ParamKind.Reporter && param.kind == ParamKind.Reporter && param.reporter != null)
                    ValidateReporter(stackId, param.reporter, registry, seenIds, diagnostics);
            }
        }

        /// <summary>An empty slot is valid (it reads as false); a filled one must hold a known condition block.</summary>
        private static void ValidateReporter(string stackId, BlockNode reporter, BlockRegistry registry,
            HashSet<string> seenIds, List<CompileDiagnostic> diagnostics)
        {
            if (!seenIds.Add(reporter.id))
                diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, reporter.id, $"Duplicate id '{reporter.id}'."));

            var def = registry.Find(reporter.blockType);
            if (def == null)
            {
                diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, reporter.id,
                    $"Unknown block type '{reporter.blockType}'."));
                return;
            }

            if (def.shape != BlockShape.Boolean)
            {
                diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, stackId, reporter.id,
                    $"'{reporter.blockType}' is not a condition, so it can't sit in a condition slot."));
                return;
            }

            ValidateParams(stackId, reporter, def, registry, seenIds, diagnostics);
        }

        public static CompileResult Link(ObjectProgram program, BlockRegistry registry)
        {
            using var _ = LinkMarker.Auto();
            var diagnostics = Validate(program, registry);
            var erroredStackIds = new HashSet<string>(
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.StackId),
                StringComparer.Ordinal);

            var code = new List<Instruction>();
            var paramTable = new List<ParamValue>();
            var debugIds = new List<string>();
            var stackEntryPoints = new int[program.stacks.Length];

            for (var i = 0; i < program.stacks.Length; i++)
            {
                var stack = program.stacks[i];
                if (erroredStackIds.Contains(stack.id) || ProgramQuery.IsLoose(stack))
                {
                    stackEntryPoints[i] = -1;
                    continue;
                }

                stackEntryPoints[i] = code.Count;
                EmitSequence(stack.sequence, registry, code, paramTable, debugIds);
            }

            var compiled = new CompiledProgram(code.ToArray(), paramTable.ToArray(), stackEntryPoints, debugIds.ToArray());
            return new CompileResult(compiled, diagnostics);
        }

        private static void EmitSequence(BlockNode[] sequence, BlockRegistry registry, List<Instruction> code,
            List<ParamValue> paramTable, List<string> debugIds)
        {
            foreach (var node in sequence)
                EmitNode(node, registry, code, paramTable, debugIds);
        }

        private static void EmitNode(BlockNode node, BlockRegistry registry, List<Instruction> code,
            List<ParamValue> paramTable, List<string> debugIds)
        {
            registry.TryGetOpcode(node.blockType, out var opcode);
            var def = registry.GetByOpcode(opcode);

            var paramOffset = AppendParams(node, def, registry, paramTable);

            var sourceNodeId = debugIds.Count;
            debugIds.Add(node.id);

            var instructionIndex = code.Count;
            code.Add(default); // placeholder — patched once branches (if any) are emitted below

            int jumpA = -1, jumpAExit = -1, jumpB = -1, jumpBExit = -1;
            if (node.branches.Length > 0)
            {
                jumpA = code.Count;
                EmitSequence(node.branches[0], registry, code, paramTable, debugIds);
                jumpAExit = code.Count;
            }
            if (node.branches.Length > 1)
            {
                jumpB = code.Count;
                EmitSequence(node.branches[1], registry, code, paramTable, debugIds);
                jumpBExit = code.Count;
            }

            code[instructionIndex] = new Instruction(opcode, paramOffset, def.parameters.Length, jumpA, jumpAExit, jumpB, jumpBExit, sourceNodeId);
        }

        /// <summary>
        /// Writes a block's params as one contiguous run (what the instruction's offset/count points at), then any
        /// condition in its slots appends its own run after it — so nested conditions never break contiguity.
        /// </summary>
        private static int AppendParams(BlockNode node, BlockDefinition def, BlockRegistry registry, List<ParamValue> paramTable)
        {
            var offset = paramTable.Count;
            for (var i = 0; i < def.parameters.Length; i++) paramTable.Add(default);

            for (var i = 0; i < def.parameters.Length; i++)
            {
                var spec = def.parameters[i];
                var param = Array.Find(node.parameters, p => p.key == spec.key);
                paramTable[offset + i] = spec.kind == ParamKind.Reporter
                    ? ResolveReporter(param, registry, paramTable)
                    : ResolveParamValue(param, spec);
            }

            return offset;
        }

        private static ParamValue ResolveReporter(BlockParam param, BlockRegistry registry, List<ParamValue> paramTable)
        {
            // Old checkbox-style conditions never get here: ProgramUpgrades converts them at every load point.
            if (param.reporter == null || !registry.TryGetOpcode(param.reporter.blockType, out var opcode)) return ParamValue.EmptyReporter;

            var def = registry.GetByOpcode(opcode);
            var offset = AppendParams(param.reporter, def, registry, paramTable);
            return ParamValue.Reporter(opcode, offset, def.parameters.Length);
        }

        private static ParamValue ResolveParamValue(BlockParam param, ParamSpec spec)
        {
            switch (spec.kind)
            {
                case ParamKind.Number:
                    return new ParamValue(ParamKind.Number, param.number, null, false, -1);
                case ParamKind.Bool:
                    return new ParamValue(ParamKind.Bool, 0, null, param.boolean, -1);
                case ParamKind.Choice:
                    var choiceIndex = Array.FindIndex(spec.choices, c => c.stableId == param.text);
                    return new ParamValue(ParamKind.Choice, 0, param.text, false, choiceIndex);
                default: // Text, ObjectRef
                    return new ParamValue(spec.kind, 0, param.text, false, -1);
            }
        }
    }
}
