using System;
using Blocky.Data;
using UnityEngine;

namespace Blocky.Compiler
{
    /// <summary>
    /// What one level lets the learner use: which blocks the palette offers, and how many blocks a program may have
    /// ("solve it in 5 blocks"). Made with <c>Assets › Create › Blocky › Toolbox</c> and set on the in-game editor
    /// (<c>BlockyInGamePanel</c>) in each level's scene. Blocks already in a program keep running whatever the
    /// toolbox says — it limits what can be added, not what runs.
    /// <para>
    /// With <see cref="Show.OnlyThese"/>, remember the hats: a learner who can't take a <c>when Go clicked</c> out of
    /// the palette can't start a script — list it, or give the object a starting script that already has one.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Blocky/Toolbox", fileName = "Toolbox", order = 1)]
    public sealed class BlockyToolbox : ScriptableObject
    {
        public enum Show
        {
            /// <summary>Only the categories and blocks listed.</summary>
            OnlyThese,

            /// <summary>Every block except the categories and blocks listed.</summary>
            AllExceptThese
        }

        [Tooltip("Show only the categories and blocks listed below, or every block except them.")]
        public Show show = Show.OnlyThese;

        [Tooltip("Whole categories of blocks.")]
        public BlockCategory[] categories = Array.Empty<BlockCategory>();

        [Tooltip("Single blocks — drag them in from Assets/Resources/Blocks.")]
        public BlockDefinition[] blocks = Array.Empty<BlockDefinition>();

        [Tooltip("How many blocks a program may use: hats (event blocks on top) don't count. 0 means no limit.")]
        [Min(0)]
        public int blockLimit;

        /// <summary>A limit is set: <see cref="blockLimit"/> is above 0.</summary>
        public bool HasLimit => blockLimit > 0;

        /// <summary>Whether the palette offers <paramref name="definition"/>.</summary>
        public bool Allows(BlockDefinition definition)
        {
            if (definition == null) return false;
            var listed = Array.IndexOf(categories ?? Array.Empty<BlockCategory>(), definition.category) >= 0 || Lists(definition);
            return show == Show.OnlyThese ? listed : !listed;
        }

        /// <summary>
        /// Whether one more block may be added to <paramref name="program"/>: always for a hat (hats don't count), and
        /// otherwise while the program is under the limit.
        /// </summary>
        public bool CanAdd(ObjectProgram program, BlockDefinition definition) =>
            !HasLimit || (definition != null && definition.shape == BlockShape.Trigger) || ProgramQuery.CountBlocks(program) < blockLimit;

        // Compared by block type rather than by reference, so a copy of a block asset (or one loaded another way) still counts.
        private bool Lists(BlockDefinition definition)
        {
            if (blocks == null) return false;
            foreach (var listed in blocks)
                if (listed != null && listed.blockType == definition.blockType) return true;
            return false;
        }
    }
}
