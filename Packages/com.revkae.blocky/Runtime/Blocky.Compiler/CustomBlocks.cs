using System;
using System.Collections.Generic;
using Blocky.Data;

namespace Blocky.Compiler
{
    /// <summary>
    /// Scratch's "My Blocks" (ADR-029). The blocks are ordinary assets, but the compiler, the advice, the runner and
    /// the palette need to recognise two of them: a <c>define</c> script is never started by an event, and a
    /// <c>run</c> block finds its definition by name. Names match the way variable and message names do — trimmed,
    /// ignoring case — and a definition belongs to the object whose program it is in, as a custom block belongs to
    /// its sprite in Scratch.
    /// </summary>
    public static class CustomBlocks
    {
        /// <summary>The hat: <c>define [name]</c>. Its script runs only when a <c>run</c> block calls it.</summary>
        public const string DefineType = "custom.define";

        /// <summary>The statement: <c>run [name] a () b () c ()</c>.</summary>
        public const string RunType = "custom.run";

        /// <summary>The input both blocks name the custom block with.</summary>
        public const string NameKey = "name";

        /// <summary>
        /// How many values a <c>run</c> block hands its definition (inputs a, b, c; read back with <c>input a</c>…).
        /// Fixed rather than declared per definition: a block's inputs come from its asset, and a call whose inputs
        /// depended on another script would need the palette, the compiler and the saves to know about shapes that change.
        /// </summary>
        public const int InputCount = 3;

        /// <summary>True for a script under a <c>define</c> hat.</summary>
        public static bool IsDefinition(BlockStack stack) => stack != null && stack.triggerBlockType == DefineType;

        /// <summary>The name a <c>define</c> script gives its block, trimmed; "" when it has none.</summary>
        public static string DefinedName(BlockStack stack)
        {
            var param = Array.Find(stack.triggerParameters ?? Array.Empty<BlockParam>(), p => p.key == NameKey);
            return param?.text?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// The name a <c>run</c> block asks for when it is typed in, trimmed; "" when it is empty. Null when a block
        /// sits in the name input — then the name is only known while the program runs.
        /// </summary>
        public static string RunName(BlockNode node)
        {
            var param = Array.Find(node.parameters ?? Array.Empty<BlockParam>(), p => p.key == NameKey);
            if (param?.reporter != null) return null;
            return param?.text?.Trim() ?? string.Empty;
        }

        /// <summary>Whether two names mean the same custom block: trimmed, ignoring case. Allocates nothing.</summary>
        public static bool SameName(string a, string b) =>
            (a ?? string.Empty).AsSpan().Trim().Equals((b ?? string.Empty).AsSpan().Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The custom blocks <paramref name="program"/> defines, each name once, in the order the scripts are —
        /// what the palette offers as ready-made <c>run</c> blocks. Definitions without a name are left out.
        /// </summary>
        public static List<string> DefinedNames(ObjectProgram program)
        {
            var names = new List<string>();
            if (program?.stacks == null) return names;

            foreach (var stack in program.stacks)
            {
                if (!IsDefinition(stack)) continue;
                var name = DefinedName(stack);
                if (name.Length > 0 && !names.Exists(n => SameName(n, name))) names.Add(name);
            }
            return names;
        }
    }
}
