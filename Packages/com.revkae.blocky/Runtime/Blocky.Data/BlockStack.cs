using System;
using UnityEngine;

namespace Blocky.Data
{
    /// <summary>A trigger block plus the vertical sequence beneath it. The unit of execution. TDD §4.2.</summary>
    [Serializable]
    public sealed class BlockStack
    {
        public string id;
        public string triggerBlockType;
        public BlockParam[] triggerParameters = Array.Empty<BlockParam>();
        public BlockNode[] sequence = Array.Empty<BlockNode>();
        public Vector2 canvasPosition; // authoring only, ignored by the VM
    }
}
