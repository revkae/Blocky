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
                    var prototype = new Label(BlockView.DisplayName(def));
                    prototype.AddToClassList("blocky-block");
                    prototype.AddToClassList(BlockClasses.Category(def.category));
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
        public static BlockNode InstantiatePrototype(BlockDefinition definition) => BlockNodes.Instantiate(definition);
    }
}
