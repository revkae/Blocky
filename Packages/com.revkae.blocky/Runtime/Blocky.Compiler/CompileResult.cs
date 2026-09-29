using System.Collections.Generic;
using System.Linq;

namespace Blocky.Compiler
{
    public sealed class CompileResult
    {
        public readonly CompiledProgram Program;
        public readonly IReadOnlyList<CompileDiagnostic> Diagnostics;

        public CompileResult(CompiledProgram program, IReadOnlyList<CompileDiagnostic> diagnostics)
        {
            Program = program;
            Diagnostics = diagnostics;
        }

        public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}
