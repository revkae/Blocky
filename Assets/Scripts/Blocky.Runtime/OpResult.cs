namespace Blocky.Runtime
{
    /// <summary>What an op wants the VM to do next. TDD §6.3.</summary>
    public enum OpResult
    {
        Continue,   // advance pc, run again this same tick if budget allows
        YieldFrame, // advance pc, resume next game frame
        Retry,      // do not advance pc, call again next frame (Wait, glide, long motion)
        Jump,       // op wrote ctx.NextPc
        Fail        // runtime error; thread halts, logged with SourceNodeId
    }
}
