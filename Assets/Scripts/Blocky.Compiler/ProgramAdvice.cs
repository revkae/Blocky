using System;
using System.Collections.Generic;
using System.Globalization;
using Blocky.Data;
using Blocky.Localization;

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
    /// Messages are in the player's language (<see cref="BlockyText"/>, <c>advice.*</c>), each one a whole sentence
    /// with the block names as placeholders, so a translation can put them where its grammar wants them.
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
                    advice.Add(new Advice(AdviceKind.Problem, stack.id, null, BlockyText.Get("advice.event_missing")));
                    continue;
                }

                if (stack.sequence.Length == 0)
                    advice.Add(new Advice(AdviceKind.Hint, stack.id, null, BlockyText.Format("advice.empty_script", trigger.DisplayName)));

                AdviseSequence(stack.id, stack.sequence, registry, advice);
            }

            AdviseCustomBlocks(program, registry, advice);
            AddUnexplainedErrors(program, registry, advice);
            return advice;
        }

        /// <summary>
        /// The mistakes custom blocks invite (ADR-029) — a <c>define</c> with no name, two with the same name, a
        /// <c>run</c> whose name no <c>define</c> on this object has, an <c>input</c> block outside any definition.
        /// Each of those runs, but does nothing, which is exactly the kind of bug a learner can't see.
        /// </summary>
        private static void AdviseCustomBlocks(ObjectProgram program, BlockRegistry registry, List<Advice> advice)
        {
            var define = registry.Find(CustomBlocks.DefineType);
            if (define == null) return; // a catalog without My Blocks
            var runName = registry.Find(CustomBlocks.RunType)?.DisplayName ?? CustomBlocks.RunType;

            var defined = new List<string>();
            foreach (var stack in program.stacks)
            {
                if (!CustomBlocks.IsDefinition(stack)) continue;

                var name = CustomBlocks.DefinedName(stack);
                if (name.Length == 0)
                    advice.Add(new Advice(AdviceKind.Hint, stack.id, null, BlockyText.Format("advice.define_no_name", define.DisplayName, runName)));
                else if (defined.Exists(n => CustomBlocks.SameName(n, name)))
                    advice.Add(new Advice(AdviceKind.Hint, stack.id, null, BlockyText.Format("advice.define_duplicate", define.DisplayName, name)));
                else
                    defined.Add(name);
            }

            foreach (var stack in program.stacks)
            {
                if (ProgramQuery.IsLoose(stack)) continue; // loose blocks never run; they have their own hint
                var insideDefinition = CustomBlocks.IsDefinition(stack);

                ForEachBlock(stack.sequence, (node, shownOn) =>
                {
                    string message = null;
                    if (node.blockType == CustomBlocks.RunType)
                    {
                        var name = CustomBlocks.RunName(node); // null: a block in the name input, known only while running
                        if (name?.Length == 0) message = BlockyText.Format("advice.run_no_name", define.DisplayName);
                        else if (name != null && !defined.Exists(n => CustomBlocks.SameName(n, name)))
                            message = BlockyText.Format("advice.run_unknown", define.DisplayName, name);
                    }
                    else if (!insideDefinition && IsCustomBlockInput(node.blockType))
                    {
                        var input = registry.Find(node.blockType);
                        if (input != null) message = BlockyText.Format("advice.input_outside", input.DisplayName, define.DisplayName);
                    }

                    if (message != null && !advice.Exists(a => a.StackId == stack.id && a.NodeId == shownOn.id && a.Message == message))
                        advice.Add(new Advice(AdviceKind.Hint, stack.id, shownOn.id, message));
                });
            }
        }

        private static bool IsCustomBlockInput(string blockType) => blockType != null && blockType.StartsWith("custom.input_", StringComparison.Ordinal);

        /// <summary>
        /// Every block of a sequence — nested branches, and blocks sitting in inputs, to any depth — with the block in
        /// the sequence that holds it (<c>shownOn</c>): the one the table outlines, so the one a message is pinned to.
        /// </summary>
        private static void ForEachBlock(BlockNode[] sequence, Action<BlockNode, BlockNode> visit)
        {
            foreach (var node in sequence)
            {
                visit(node, node);
                ForEachBlockInInputs(node, node, visit);
                foreach (var branch in node.branches) ForEachBlock(branch, visit);
            }
        }

        private static void ForEachBlockInInputs(BlockNode node, BlockNode shownOn, Action<BlockNode, BlockNode> visit)
        {
            foreach (var param in node.parameters)
            {
                if (param?.reporter == null) continue;
                visit(param.reporter, shownOn);
                ForEachBlockInInputs(param.reporter, shownOn, visit);
            }
        }

        private static void AdviseLoose(BlockStack stack, BlockRegistry registry, List<Advice> advice)
        {
            if (stack.sequence.Length == 0) return;

            var first = stack.sequence[0];
            var definition = registry.Find(first.blockType);
            if (stack.sequence.Length == 1 && definition != null && definition.shape == BlockShape.Boolean)
            {
                var host = FirstBlockWithConditionHole(registry);
                var message = host != null
                    ? BlockyText.Format("advice.loose_condition.example", definition.DisplayName, host.DisplayName)
                    : BlockyText.Format("advice.loose_condition", definition.DisplayName);
                advice.Add(new Advice(AdviceKind.Hint, stack.id, first.id, message));
                return;
            }

            var go = registry.Find(GoTriggerType);
            var hint = go != null ? BlockyText.Format("advice.loose_blocks.go", go.DisplayName) : BlockyText.Get("advice.loose_blocks");
            advice.Add(new Advice(AdviceKind.Hint, stack.id, first.id, hint));
        }

        private static void AdviseSequence(string stackId, BlockNode[] sequence, BlockRegistry registry, List<Advice> advice)
        {
            foreach (var node in sequence)
            {
                var definition = registry.Find(node.blockType);
                if (definition == null)
                {
                    advice.Add(new Advice(AdviceKind.Problem, stackId, node.id, BlockyText.Get("advice.block_missing")));
                    continue;
                }

                AdviseParams(stackId, node, node, definition, registry, advice);

                if (definition.branchCount > 0 && AllBranchesEmpty(node))
                    advice.Add(new Advice(AdviceKind.Hint, stackId, node.id, BlockyText.Format("advice.empty_branch", definition.DisplayName)));

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
                    advice.Add(new Advice(AdviceKind.Problem, stackId, shownOn.id, BlockyText.Format("advice.missing_value", name, spec.DisplayName)));
                    continue;
                }

                switch (spec.kind)
                {
                    case ParamKind.Number when param.number < spec.min || param.number > spec.max:
                        advice.Add(new Advice(AdviceKind.Problem, stackId, shownOn.id,
                            BlockyText.Format("advice.out_of_range", spec.DisplayName, name, Format(spec.min), Format(spec.max))));
                        break;

                    case ParamKind.Choice when Array.FindIndex(spec.choices, c => c.stableId == param.text) < 0:
                        advice.Add(new Advice(AdviceKind.Problem, stackId, shownOn.id,
                            BlockyText.Format("advice.bad_choice", param.text, spec.DisplayName, name)));
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
                advice.Add(new Advice(AdviceKind.Hint, stackId, owner.id, BlockyText.Format("advice.empty_hole", ownerName, BlockyText.Get("value.false"))));
                return;
            }

            var definition = registry.Find(condition.blockType);
            if (definition == null)
            {
                advice.Add(new Advice(AdviceKind.Problem, stackId, owner.id, BlockyText.Get("advice.condition_missing")));
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

                advice.Add(new Advice(AdviceKind.Problem, diagnostic.StackId, shownOn, BlockyText.Get("advice.something_wrong")));
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
