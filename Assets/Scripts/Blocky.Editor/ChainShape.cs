using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;

namespace Blocky.Editor
{
    /// <summary>
    /// What a dragged chain's silhouette allows, worked out in one place from the model (the nodes and the
    /// trigger, looked up in the registry) rather than from whichever views happen to be on screen:
    /// a hat has no top connector, a cap at the end has no bottom connector, and a lone condition fits only a
    /// condition slot. <see cref="SnapResolver"/> and <see cref="ChainDragSession"/> read these rules.
    /// </summary>
    public readonly struct ChainShape
    {
        public readonly bool HasHat;
        public readonly bool EndsWithCap;
        public readonly bool IsCondition;

        private ChainShape(bool hasHat, bool endsWithCap, bool isCondition)
        {
            HasHat = hasHat;
            EndsWithCap = endsWithCap;
            IsCondition = isCondition;
        }

        /// <summary>A single condition block (taken out of a slot, or fresh from the palette).</summary>
        public static ChainShape Condition => new(false, false, true);

        /// <summary>A fresh block from the palette.</summary>
        public static ChainShape Of(BlockDefinition definition) => definition.shape switch
        {
            BlockShape.Trigger => new ChainShape(true, false, false),
            BlockShape.Cap => new ChainShape(false, true, false),
            BlockShape.Boolean => Condition,
            _ => default
        };

        /// <summary>A chain picked up off the table: a whole stack (with its trigger, if any) or a run of nodes.</summary>
        public static ChainShape Of(BlockRegistry registry, string triggerBlockType, IReadOnlyList<BlockNode> nodes)
        {
            var hasHat = !string.IsNullOrEmpty(triggerBlockType);
            var last = nodes.Count > 0 ? registry.Find(nodes[nodes.Count - 1].blockType) : null;
            var isCondition = !hasHat && nodes.Count == 1 && last != null && last.shape == BlockShape.Boolean;
            return new ChainShape(hasHat, last != null && last.shape == BlockShape.Cap, isCondition);
        }
    }
}
