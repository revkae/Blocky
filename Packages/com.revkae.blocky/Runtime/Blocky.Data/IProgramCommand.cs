namespace Blocky.Data
{
    /// <summary>The only write path into an <see cref="ObjectProgram"/> (TDD §4.4). Apply via <see cref="ProgramStore.Apply"/>.</summary>
    public interface IProgramCommand
    {
        void Do(ProgramStore store);
        void Undo(ProgramStore store);
        string Describe();
    }
}
