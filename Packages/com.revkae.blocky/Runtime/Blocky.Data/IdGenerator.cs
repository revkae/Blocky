using System;

namespace Blocky.Data
{
    /// <summary>
    /// 16-char base64 GUID-prefix ids (TDD §4.3). Duplication regenerates ids for the whole subtree —
    /// uniqueness is a hard compile error in <c>ProgramCompiler.Validate()</c>, not a warning.
    /// </summary>
    public static class IdGenerator
    {
        public static string NewId()
        {
            var guidBytes = Guid.NewGuid().ToByteArray();
            var prefix = new byte[12]; // 12 bytes -> exactly 16 base64 chars, no padding
            Array.Copy(guidBytes, prefix, prefix.Length);
            return Convert.ToBase64String(prefix).Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Regenerates the id of <paramref name="node"/> and every node in its branches, in place.</summary>
        public static void RegenerateSubtreeIds(BlockNode node)
        {
            if (node == null) return;
            node.id = NewId();
            foreach (var branch in node.branches)
            foreach (var child in branch)
                RegenerateSubtreeIds(child);
        }

        /// <summary>Regenerates the id of a stack and every node in its sequence, in place.</summary>
        public static void RegenerateSubtreeIds(BlockStack stack)
        {
            if (stack == null) return;
            stack.id = NewId();
            foreach (var node in stack.sequence)
                RegenerateSubtreeIds(node);
        }
    }
}
