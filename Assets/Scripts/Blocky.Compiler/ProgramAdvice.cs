using System;
using System.Collections.Generic;
using System.Globalization;
using Blocky.Data;

namespace Blocky.Compiler
{
    /// <summary>How serious a piece of <see cref="Advice"/> is.</summary>
    public enum AdviceKind
    {
        /// <summary>A legal program that probably doesn't do what was meant (loose blocks, an empty hole).</summary>
        Hint,

        /// <summary>Something that stops the script from running at all.</summary>
        Problem
    }

    /// <summary>One plain-language message, pinned to a block (<see cref="NodeId"/>) or, with a null node id, to a stack's event block.</summary>
    public readonly struct Advice
    {
        public readonly AdviceKind Kind;
        public readonly string StackId;
        public readonly string NodeId;
        public readonly string Message;

        public Advice(AdviceKind kind, string stackId, string nodeId, string message)
        {
            Kind = kind;
            StackId = stackId;
            NodeId = nodeId;
            Message = message;
        }

        public override string ToString() => $"[{Kind}] {StackId}/{NodeId}: {Message}";
    }

    /// <summary>
    /// Help for someone who can't read compiler errors — a child, or a teacher who doesn't code. Each message
    /// says what will happen ("never runs", "counts as false") and what to do about it, using the names printed
    /// on the blocks. Anything the compiler rejects that no rule here explains still gets a general message, so
    /// a script never fails to run without saying so.
    /// </summary>
    public static class ProgramAdvice
    {
        private const string GoTriggerType = "event.when_go_clicked";

        public static List<Advice> Collect(ObjectProgram program, BlockRegistry registry)
        {
            var advice = new List<Advice>();

            foreach (var stack in program.stacks)
            {
                if (ProgramQuery.IsLoose(stack))
                {
                    AdviseLoose(stack, registry, advice);
                    continue;
                }

                var trigger = registry.Find(stack.triggerBlockType);
                if (trigger == null)
                {
                    advice.Add(new Advice(AdviceKind.Problem, stack.id, null, "This event block doesn't exist any more, so this script won't run."));
                    continue;
                }

                if (stack.sequence.Length == 0)
                    advice.Add(new Advice(AdviceKind.Hint, stack.id, null,
                        $"Nothing is under “{trigger.DisplayName}” yet, so nothing happens. Snap blocks under it."));

                AdviseSequence(stack.id, stack.sequence, registry, advice);
            }

            AddUnexplainedErrors(program, registry, advice);
            return advice;
        }

        private static void AdviseLoose(BlockStack stack, BlockRegistry registry, List<Advice> advice)
        {
            if (stack.sequence.Length == 0) return;

            var first = stack.sequence[0];
            var definition = registry.Find(first.blockType);
            if (stack.sequence.Length == 1 && definition != null && definition.shape == BlockShape.Boolean)
            {
                var host = FirstBlockWithConditionHole(registry);
                var where = host != null ? $"like the one in “{host.DisplayName}”" : "in a block";
                advice.Add(new Advice(AdviceKind.Hint, stack.id, first.id, $"“{definition.DisplayName}” is a condition. It goes in a ⬡ hole, {where}."));
                return;
            }

            var go = registry.Find(GoTriggerType);
            var fix = go != null ? $"Snap them under “{go.DisplayName}”." : "Snap them under an event block.";
            advice.Add(new Advice(AdviceKind.Hint, stack.id, first.id, $"These blocks have no event on top, so they never run. {fix}"));
        }

        private static void AdviseSequence(string stackId, BlockNode[] sequence, BlockRegistry registry, List<Advice> advice)
        {
            foreach (var node in sequence)
            {
                var definition = registry.Find(node.blockType);
                if (definition == null)
                {
                    advice.Add(new Advice(AdviceKind.Problem, stackId, node.id, "This block doesn't exist any more, so this script won't run. Delete it."));
                    continue;
                }

                AdviseParams(stackId, node, node, definition, registry, advice);

                if (definition.branchCount > 0 && AllBranchesEmpty(node))
                    advice.Add(new Advice(AdviceKind.Hint, stackId, node.id, $"“{definition.DisplayName}” has nothing inside it yet. Put blocks in its gap."));

                foreach (var branch in node.branches)
                    AdviseSequence(stackId, branch, registry, advice);
            }
        }

        /// <summary>
        /// Checks <paramref name="node"/>'s values. Messages go on <paramref name="shownOn"/> — the block itself, or,
        /// for a condition sitting in a hole, the block that owns the hole (which is what the table outlines).
        /// </summary>
        private static void AdviseParams(string stackId, BlockNode node, BlockNode shownOn, BlockDefinition definition, BlockRegistry registry,
            List<Advice> advice)
        {
            var name = definition.DisplayName;
            foreach (var spec in definition.parameters)
            {
                var param = Array.Find(node.parameters, p => p.key == spec.key);
                if (param == null)
                {
                    advice.Add(new Advice(AdviceKind.Problem, stackId, shownOn.id, $"“{name}” is missing its “{spec.key}” value, so this script won't run."));
                    continue;
                }

                switch (spec.kind)
                {
                    case ParamKind.Number when param.number < spec.min || param.number > spec.max:
                        advice.Add(new Advice(AdviceKind.Problem, stackId, shownOn.id,
                            $"“{spec.key}” in “{name}” must be from {Format(spec.min)} to {Format(spec.max)}, so this script won't run."));
                        break;

                    case ParamKind.Choice when Array.FindIndex(spec.choices, c => c.stableId == param.text) < 0:
                        advice.Add(new Advice(AdviceKind.Problem, stackId, shownOn.id,
                            $"“{param.text}” isn't one of the choices for “{spec.key}” in “{name}”, so this script won't run."));
                        break;

                    case ParamKind.Reporter:
                        AdviseConditionHole(stackId, node, name, param, registry, advice);
                        break;
                }
            }
        }

        private static void AdviseConditionHole(string stackId, BlockNode owner, string ownerName, BlockParam param, BlockRegistry registry, List<Advice> advice)
        {
            var condition = param.kind == ParamKind.Reporter ? param.reporter : null;
            if (condition == null)
            {
                advice.Add(new Advice(AdviceKind.Hint, stackId, owner.id,
                    $"The ⬡ hole in “{ownerName}” is empty, and an empty hole counts as “false”. Drag a condition into it."));
                return;
            }

            var definition = registry.Find(condition.blockType);
            if (definition == null)
            {
                advice.Add(new Advice(AdviceKind.Problem, stackId, owner.id, "The condition in this ⬡ hole doesn't exist any more, so this script won't run."));
                return;
            }

            AdviseParams(stackId, condition, owner, definition, registry, advice);
        }

        /// <summary>A safety net: every compiler error in a runnable stack that no rule above put into words gets a general message.</summary>
        private static void AddUnexplainedErrors(ObjectProgram program, BlockRegistry registry, List<Advice> advice)
        {
            foreach (var diagnostic in ProgramCompiler.Validate(program, registry))
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error) continue;

                var stack = ProgramQuery.FindStack(program, diagnostic.StackId);
                if (stack == null || ProgramQuery.IsLoose(stack)) continue; // loose blocks never run anyway — they already have their hint
                // A condition's own bad value is reported on the block that holds it — the one the table outlines.
                var shownOn = diagnostic.NodeId != null &&
                              ProgramQuery.TryFindConditionOwner(program, diagnostic.StackId, diagnostic.NodeId, out var owner, out _)
                    ? owner.id
                    : diagnostic.NodeId;
                if (HasProblem(advice, diagnostic.StackId, shownOn)) continue;

                advice.Add(new Advice(AdviceKind.Problem, diagnostic.StackId, shownOn, "Something is wrong with this block, so this script won't run."));
            }
        }

        private static bool HasProblem(List<Advice> advice, string stackId, string nodeId)
        {
            foreach (var item in advice)
                if (item.Kind == AdviceKind.Problem && item.StackId == stackId && item.NodeId == nodeId) return true;
            return false;
        }

        private static bool AllBranchesEmpty(BlockNode node)
        {
            foreach (var branch in node.branches)
                if (branch.Length > 0) return false;
            return true;
        }

        private static BlockDefinition FirstBlockWithConditionHole(BlockRegistry registry)
        {
            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var definition = registry.GetByOpcode(opcode);
                if (definition.shape == BlockShape.Boolean) continue;
                foreach (var spec in definition.parameters)
                    if (spec.kind == ParamKind.Reporter) return definition;
            }
            return null;
        }

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
