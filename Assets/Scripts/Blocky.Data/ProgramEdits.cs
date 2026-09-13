using System;
using Blocky.Data.Serialization;

namespace Blocky.Data
{
    /// <summary>Shared helpers for commands whose undo is "put the program back exactly as it was".</summary>
    internal static class ProgramEdits
    {
        public static BlockStack[] Snapshot(ObjectProgram program) =>
            ProgramSerializer.Deserialize(ProgramSerializer.Serialize(program)).stacks;

        public static void RemoveStack(ObjectProgram program, string stackId)
        {
            var index = Array.FindIndex(program.stacks, s => s.id == stackId);
            if (index >= 0) program.stacks = ArrayUtil.RemoveAt(program.stacks, index, out _);
        }
    }
}
