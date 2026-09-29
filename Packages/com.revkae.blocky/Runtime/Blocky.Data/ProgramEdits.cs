using System;

namespace Blocky.Data
{
    /// <summary>Shared helpers for commands whose undo is "put the program back exactly as it was".</summary>
    internal static class ProgramEdits
    {
        /// <summary>
        /// A deep copy of every stack — node trees, params and the condition blocks inside params included — so the
        /// command can mutate the live program freely and restore it on undo or failure. Copied directly rather than
        /// through a JSON round trip: this runs on every drop and delete.
        /// </summary>
        public static BlockStack[] Snapshot(ObjectProgram program) => CopyAll(program.stacks, CopyStack);

        public static void AppendStack(ObjectProgram program, BlockStack stack) =>
            program.stacks = ArrayUtil.Insert(program.stacks, program.stacks.Length, stack);

        public static void RemoveStack(ObjectProgram program, string stackId)
        {
            var index = Array.FindIndex(program.stacks, s => s.id == stackId);
            if (index >= 0) program.stacks = ArrayUtil.RemoveAt(program.stacks, index, out _);
        }

        private static BlockStack CopyStack(BlockStack stack) => new()
        {
            id = stack.id,
            triggerBlockType = stack.triggerBlockType,
            triggerParameters = CopyAll(stack.triggerParameters, CopyParam),
            sequence = CopyAll(stack.sequence, CopyNode),
            canvasPosition = stack.canvasPosition
        };

        private static BlockNode CopyNode(BlockNode node) => node == null ? null : new BlockNode
        {
            id = node.id,
            blockType = node.blockType,
            parameters = CopyAll(node.parameters, CopyParam),
            branches = CopyAll(node.branches, branch => CopyAll(branch, CopyNode))
        };

        private static BlockParam CopyParam(BlockParam param) => param == null ? null : new BlockParam
        {
            key = param.key,
            kind = param.kind,
            number = param.number,
            text = param.text,
            boolean = param.boolean,
            reporter = CopyNode(param.reporter)
        };

        private static T[] CopyAll<T>(T[] items, Converter<T, T> copy) => items == null ? null : Array.ConvertAll(items, copy);
    }
}
