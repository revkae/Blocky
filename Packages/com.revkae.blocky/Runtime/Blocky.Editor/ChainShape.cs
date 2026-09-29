using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;

namespace Blocky.Editor
{
    /// <summary>
    /// What a dragged chain's silhouette allows, worked out in one place from the model (the nodes and the
    /// trigger, looked up in the registry) rather than from whichever views happen to be on screen:
    /// a hat has no top connector, a cap at the end has no bottom connector, a lone condition fits only a
    /// hexagonal hole, and a lone reporter fits any value input. <see cref="SnapResolver"/> and <see cref="ChainDragSession"/> read these rules.
    /// </summary>
    public readonly struct ChainShape
    {
        public readonly bool HasHat;
        public readonly bool EndsWithCap;
        public readonly bool IsCondition;

        /// <summary>A lone reporter (Scratch's round block): it fits a value input, but never a hexagonal hole.</summary>
        public readonly bool IsReporter;

        /// <summary>This chain belongs inside another block's input rather than in a sequence.</summary>
        public bool FitsInSlot => IsCondition || IsReporter;

        private ChainShape(bool hasHat, bool endsWithCap, bool isCondition, bool isReporter = false)
        {
            HasHat = hasHat;
            EndsWithCap = endsWithCap;
            IsCondition = isCondition;
            IsReporter = isReporter;
        }

        /// <summary>A single condition block (taken out of a slot, or fresh from the palette).</summary>
        public static ChainShape Condition => new(false, false, true);

        /// <summary>A single reporter block.</summary>
        public static ChainShape Reporter => new(false, false, false, true);

        /// <summary>A fresh block from the palette.</summary>
        public static ChainShape Of(BlockDefinition definition) => definition.shape switch
        {
            BlockShape.Trigger => new ChainShape(true, false, false),
            BlockShape.Cap => new ChainShape(false, true, false),
            BlockShape.Boolean => Condition,
            BlockShape.Reporter => Reporter,
            _ => default
        };

        /// <summary>A chain picked up off the table: a whole stack (with its trigger, if any) or a run of nodes.</summary>
        public static ChainShape Of(BlockRegistry registry, string triggerBlockType, IReadOnlyList<BlockNode> nodes)
        {
            var hasHat = !string.IsNullOrEmpty(triggerBlockType);
            var last = nodes.Count > 0 ? registry.Find(nodes[nodes.Count - 1].blockType) : null;
            var alone = !hasHat && nodes.Count == 1 && last != null;
            return new ChainShape(hasHat, last != null && last.shape == BlockShape.Cap,
                alone && last.shape == BlockShape.Boolean, alone && last.shape == BlockShape.Reporter);
        }
    }
}
