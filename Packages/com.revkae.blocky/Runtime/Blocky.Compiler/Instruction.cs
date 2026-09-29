namespace Blocky.Compiler
{
    /// <summary>
    /// One linked instruction. <see cref="JumpA"/>/<see cref="JumpB"/> are the entry pc of branch 0/1 (-1 if the
    /// block has no such branch); <see cref="JumpAExit"/>/<see cref="JumpBExit"/> are the pc immediately after
    /// that branch's last instruction. Entry+exit pairs are the compiler's contribution to control flow — the
    /// Runtime VM (Milestone 3) decides what to do with them (fall through, loop back, skip) via <c>OpResult</c>
    /// and per-thread <c>Frame</c>s; see decision ADR-002 in the vault.
    /// </summary>
    public readonly struct Instruction
    {
        public readonly int Opcode;
        public readonly int ParamOffset;
        public readonly int ParamCount;
        public readonly int JumpA;
        public readonly int JumpAExit;
        public readonly int JumpB;
        public readonly int JumpBExit;
        public readonly int SourceNodeId; // index into CompiledProgram.DebugNodeIds

        public Instruction(int opcode, int paramOffset, int paramCount, int jumpA, int jumpAExit, int jumpB, int jumpBExit, int sourceNodeId)
        {
            Opcode = opcode;
            ParamOffset = paramOffset;
            ParamCount = paramCount;
            JumpA = jumpA;
            JumpAExit = jumpAExit;
            JumpB = jumpB;
            JumpBExit = jumpBExit;
            SourceNodeId = sourceNodeId;
        }
    }
}
