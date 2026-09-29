using System;

namespace Blocky.Data
{
    /// <summary>
    /// One authored instruction. <see cref="branches"/> is jagged (one entry per body cavity) rather than a
    /// single body list, so If/IfElse/future multi-branch blocks need no special-casing. See TDD §4.2.
    /// </summary>
    [Serializable]
    public sealed class BlockNode
    {
        public string id;
        public string blockType;
        public BlockParam[] parameters = Array.Empty<BlockParam>();
        public BlockNode[][] branches = Array.Empty<BlockNode[]>();
    }
}
