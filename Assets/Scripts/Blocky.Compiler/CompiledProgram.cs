namespace Blocky.Compiler
{
    /// <summary>
    /// Immutable and cacheable — identical <c>ObjectProgram</c>s across many objects share one instance
    /// (TDD §6.2). After linking, nothing here needs a string or a dictionary lookup to execute.
    /// </summary>
    public sealed class CompiledProgram
    {
        public readonly Instruction[] Code;
        public readonly ParamValue[] ParamTable;
        public readonly int[] StackEntryPoints; // pc per stack, in program order; -1 if that stack failed to compile

        /// <summary>
        /// The pc just past each stack's last instruction (-1 where the entry point is). Stacks are emitted one
        /// after another into one <see cref="Code"/> array, so without this a script that simply ends would run
        /// straight on into the next script's blocks — see ADR-019 in the vault.
        /// </summary>
        public readonly int[] StackExitPoints;
        public readonly string[] DebugNodeIds;  // strip in release builds

        public CompiledProgram(Instruction[] code, ParamValue[] paramTable, int[] stackEntryPoints, string[] debugNodeIds,
            int[] stackExitPoints = null)
        {
            Code = code;
            ParamTable = paramTable;
            StackEntryPoints = stackEntryPoints;
            DebugNodeIds = debugNodeIds;
            StackExitPoints = stackExitPoints;
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
