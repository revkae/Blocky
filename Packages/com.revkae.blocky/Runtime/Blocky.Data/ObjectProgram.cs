using System;
using Blocky.Data.Serialization;

namespace Blocky.Data
{
    /// <summary>All stacks belonging to one target object. TDD §4.2.</summary>
    [Serializable]
    public sealed class ObjectProgram
    {
        public int schemaVersion = ProgramSchema.CurrentVersion;
        public string targetObjectUid; // resolved to a GameObject once at link time; never resolved by a block itself
        public BlockStack[] stacks = Array.Empty<BlockStack>();
    }
}
