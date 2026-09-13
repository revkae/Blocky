using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Blocky.Compiler
{
    /// <summary>
    /// Opcodes are assigned once, by sorted <c>blockType</c> — stable across builds so serialized debug data
    /// stays comparable (TDD §6.2, §7). Built once at boot; a dictionary here is fine, it never runs per-frame.
    /// </summary>
    public sealed class BlockRegistry
    {
        private readonly BlockDefinition[] _byOpcode;
        private readonly Dictionary<string, int> _opcodeByBlockType;

        private BlockRegistry(BlockDefinition[] byOpcode, Dictionary<string, int> opcodeByBlockType)
        {
            _byOpcode = byOpcode;
            _opcodeByBlockType = opcodeByBlockType;
        }

        public int Count => _byOpcode.Length;

        public BlockDefinition GetByOpcode(int opcode) => _byOpcode[opcode];

        public bool TryGetOpcode(string blockType, out int opcode) => _opcodeByBlockType.TryGetValue(blockType, out opcode);

        public BlockDefinition Find(string blockType) =>
            TryGetOpcode(blockType, out var opcode) ? _byOpcode[opcode] : null;

        public static BlockRegistry Build(IEnumerable<BlockDefinition> definitions)
        {
            var list = definitions.Where(d => d != null).ToList();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var d in list)
            {
                if (string.IsNullOrEmpty(d.blockType))
                    throw new InvalidOperationException($"BlockDefinition '{d.name}' has no blockType.");
                if (d.blockType != d.blockType.ToLowerInvariant())
                    throw new InvalidOperationException($"blockType '{d.blockType}' must be lowercase invariant (TDD §7).");
                if (!seen.Add(d.blockType))
                    throw new InvalidOperationException($"Duplicate blockType '{d.blockType}'.");
            }

            list.Sort((a, b) => string.CompareOrdinal(a.blockType, b.blockType));

            var byOpcode = list.ToArray();
            var opcodeByBlockType = new Dictionary<string, int>(byOpcode.Length, StringComparer.Ordinal);
            for (var i = 0; i < byOpcode.Length; i++)
                opcodeByBlockType[byOpcode[i].blockType] = i;

            return new BlockRegistry(byOpcode, opcodeByBlockType);
        }

        /// <summary>Scans <c>Assets/Resources/&lt;path&gt;</c>. Works identically in the Editor and in a build.</summary>
        public static BlockRegistry LoadFromResources(string path = "Blocks") =>
            Build(Resources.LoadAll<BlockDefinition>(path));
    }
}
