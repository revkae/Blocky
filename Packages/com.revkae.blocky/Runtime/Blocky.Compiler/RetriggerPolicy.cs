namespace Blocky.Compiler
{
    /// <summary>How a trigger reacts to firing again while its stack's thread is still running. TDD §6.4.</summary>
    public enum RetriggerPolicy
    {
        RestartOnRetrigger,
        IgnoreWhileRunning,
        AllowConcurrent
    }
}
