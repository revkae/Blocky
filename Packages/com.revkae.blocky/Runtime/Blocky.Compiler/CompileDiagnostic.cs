namespace Blocky.Compiler
{
    public enum DiagnosticSeverity
    {
        Error,
        Warning
    }

    /// <summary>One compiler finding. <see cref="NodeId"/> is null for a stack-level issue (bad trigger type, duplicate stack id).</summary>
    public readonly struct CompileDiagnostic
    {
        public readonly DiagnosticSeverity Severity;
        public readonly string StackId;
        public readonly string NodeId;
        public readonly string Message;

        public CompileDiagnostic(DiagnosticSeverity severity, string stackId, string nodeId, string message)
        {
            Severity = severity;
            StackId = stackId;
            NodeId = nodeId;
            Message = message;
        }

        public override string ToString() => $"[{Severity}] stack {StackId} node {NodeId}: {Message}";
    }
}
