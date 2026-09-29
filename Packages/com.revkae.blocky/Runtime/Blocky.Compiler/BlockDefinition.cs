using System;
using Blocky.Localization;
using UnityEngine;

namespace Blocky.Compiler
{
    /// <summary>
    /// A block's authoring-time contract. Adding a block is this asset + one <c>IBlockOp</c> class — no editor
    /// or serialization code changes (TDD §7). <see cref="blockType"/> must be a lowercase-invariant, stable id;
    /// never apply culture-sensitive casing to it (Turkish-I problem, TDD §7).
    /// </summary>
    [CreateAssetMenu(menuName = "Blocky/Block Definition", fileName = "NewBlockDefinition")]
    public sealed class BlockDefinition : ScriptableObject
    {
        public string blockType;
        public string displayNameKey;
        public BlockShape shape;
        public BlockCategory category;
        public int branchCount;
        public ParamSpec[] parameters = Array.Empty<ParamSpec>();
        public string executorKey;
        public RetriggerPolicy retrigger = RetriggerPolicy.RestartOnRetrigger;

        /// <summary>
        /// The name a player sees on the block, in their language ("Repeat", "Tekrarla"): the <c>block.&lt;blockType&gt;</c>
        /// string. A block no language file names yet shows <see cref="displayNameKey"/> (its English name), else its type.
        /// </summary>
        public string DisplayName => BlockyText.Get("block." + blockType, string.IsNullOrEmpty(displayNameKey) ? blockType : displayNameKey);
    }
}
