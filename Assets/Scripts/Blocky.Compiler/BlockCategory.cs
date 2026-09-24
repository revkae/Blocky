namespace Blocky.Compiler
{
    /// <summary>Drives block color via a USS class in the editor (TDD §7, §9). Extend as the catalog grows — append only, assets store the number.</summary>
    public enum BlockCategory
    {
        Event,
        Motion,
        Looks,
        Control,
        Conditions,

        /// <summary>What the object can feel: the keyboard, contacts, the timer (Scratch's Sensing).</summary>
        Sensing,

        /// <summary>Blocks that combine or compute values — <c>and</c>, <c>or</c>, <c>not</c> (Scratch's Operators).</summary>
        Operators,

        /// <summary>Playing and stopping sound.</summary>
        Sound,

        /// <summary>Named values the program can set and read back.</summary>
        Variables
    }
}
