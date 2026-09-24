namespace Blocky.Compiler
{
    /// <summary>
    /// Immutable and cacheable — identical <c>ObjectProgram</c>s across many objects share one instance
    /// (TDD §6.2). After linking, nothing here needs a dictionary lookup to execute; the one string compare left is
    /// a <c>run</c> block finding its custom block by name (<see cref="TryFindProcedure"/>), because the name may be
    /// worked out while the program runs, like a variable's or a message's.
    /// </summary>
    public sealed class CompiledProgram
    {
        public readonly Instruction[] Code;
        public readonly ParamValue[] ParamTable;
        public readonly int[] StackEntryPoints; // pc per stack, in program order; -1 if that stack failed to compile, is loose or has no blocks

        /// <summary>
        /// The pc just past each stack's last instruction (-1 where the entry point is). Stacks are emitted one
        /// after another into one <see cref="Code"/> array, so without this a script that simply ends would run
        /// straight on into the next script's blocks — see ADR-019 in the vault.
        /// </summary>
        public readonly int[] StackExitPoints;
        public readonly string[] DebugNodeIds;  // strip in release builds

        /// <summary>
        /// Per stack: the custom block it defines (<c>define [name]</c>, trimmed), or null — for every other stack,
        /// for a definition with no blocks or that failed to compile, and for one whose name an earlier stack
        /// already defines (the first one wins, and the advice says so).
        /// </summary>
        public readonly string[] ProcedureNames;

        public CompiledProgram(Instruction[] code, ParamValue[] paramTable, int[] stackEntryPoints, string[] debugNodeIds,
            int[] stackExitPoints = null, string[] procedureNames = null)
        {
            Code = code;
            ParamTable = paramTable;
            StackEntryPoints = stackEntryPoints;
            DebugNodeIds = debugNodeIds;
            StackExitPoints = stackExitPoints;
            ProcedureNames = procedureNames;
        }

        /// <summary>
        /// Where the custom block <paramref name="name"/> is: its first pc and the pc just past its last block. False
        /// when this program has no runnable definition by that name — then a <c>run</c> block does nothing.
        /// Names match trimmed and ignoring case; allocates nothing.
        /// </summary>
        public bool TryFindProcedure(string name, out int entryPc, out int exitPc)
        {
            entryPc = exitPc = -1;
            if (ProcedureNames == null || StackExitPoints == null) return false;

            for (var i = 0; i < ProcedureNames.Length; i++)
            {
                if (ProcedureNames[i] == null || !CustomBlocks.SameName(ProcedureNames[i], name)) continue;
                entryPc = StackEntryPoints[i];
                exitPc = StackExitPoints[i];
                return true;
            }
            return false;
        }

        /// <summary>Where the stack entered at <paramref name="entryPc"/> ends — its own last instruction, not the program's.</summary>
        public int ExitPcFor(int entryPc)
        {
            if (StackExitPoints == null) return Code.Length;
            for (var i = 0; i < StackEntryPoints.Length; i++)
                if (StackEntryPoints[i] == entryPc) return StackExitPoints[i];
            return Code.Length;
        }
    }
}
