using System;
using Blocky.Data;

namespace Blocky.Compiler
{
    /// <summary>Creates fresh nodes from definitions — the one place that knows how a new block's params and branches start out.</summary>
    public static class BlockNodes
    {
        /// <summary>
        /// A new node for <paramref name="definition"/>: a fresh id, every param at its spec's default, and one empty
        /// sequence per branch (TDD §4.3, §8.2). Condition slots start empty.
        /// </summary>
        public static BlockNode Instantiate(BlockDefinition definition)
        {
            var node = new BlockNode
            {
                id = IdGenerator.NewId(),
                blockType = definition.blockType,
                parameters = new BlockParam[definition.parameters.Length],
                branches = new BlockNode[definition.branchCount][]
            };

            for (var i = 0; i < definition.parameters.Length; i++)
            {
                var spec = definition.parameters[i];
                var text = spec.defaultText;
                // An empty choice fails validation, which silently skips the whole stack — default to the first option.
                if (spec.kind == ParamKind.Choice && string.IsNullOrEmpty(text) && spec.choices.Length > 0)
                    text = spec.choices[0].stableId;

                node.parameters[i] = new BlockParam
                {
                    key = spec.key,
                    kind = spec.kind,
                    number = spec.defaultNumber,
                    text = text
                };
            }

            for (var b = 0; b < definition.branchCount; b++)
                node.branches[b] = Array.Empty<BlockNode>();

            return node;
        }
    }
}
