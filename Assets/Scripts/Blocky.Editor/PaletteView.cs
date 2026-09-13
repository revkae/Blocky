using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>Displays block prototypes grouped by category (TDD §8.2).</summary>
    public sealed class PaletteView : VisualElement
    {
        public PaletteView(BlockRegistry registry)
        {
            AddToClassList("blocky-palette");

            var byCategory = new SortedDictionary<string, List<BlockDefinition>>(StringComparer.Ordinal);
            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var def = registry.GetByOpcode(opcode);
                var key = def.category.ToString();
                if (!byCategory.TryGetValue(key, out var list)) byCategory[key] = list = new List<BlockDefinition>();
                list.Add(def);
            }

            foreach (var (category, defs) in byCategory)
            {
                var section = new VisualElement();
                section.AddToClassList("blocky-palette__section");
                section.Add(new Label(category));

                foreach (var def in defs)
                {
                    var prototype = new Label(string.IsNullOrEmpty(def.displayNameKey) ? def.blockType : def.displayNameKey);
                    prototype.AddToClassList("blocky-block");
                    prototype.AddToClassList($"blocky-block--category-{def.category.ToString().ToLowerInvariant()}");
                    prototype.AddToClassList("blocky-palette__prototype");
                    prototype.userData = def; // read by a drag-out manipulator to know which definition was picked
                    section.Add(prototype);
                }

                Add(section);
            }
        }

        /// <summary>
        /// Clones a fresh node from a prototype, regenerating its id and filling params from each spec's
        /// defaults (TDD §4.3, §8.2) — this is what a palette drag-out produces before an <c>InsertNode</c>
        /// command places it.
        /// </summary>
        public static BlockNode InstantiatePrototype(BlockDefinition definition)
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
