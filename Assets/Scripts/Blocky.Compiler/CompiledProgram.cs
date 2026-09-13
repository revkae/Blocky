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
        public readonly string[] DebugNodeIds;  // strip in release builds

        public CompiledProgram(Instruction[] code, ParamValue[] paramTable, int[] stackEntryPoints, string[] debugNodeIds)
        {
            Code = code;
            ParamTable = paramTable;
            StackEntryPoints = stackEntryPoints;
            DebugNodeIds = debugNodeIds;
        }
    }
}
