namespace Blocky.Runtime
{
    /// <summary>
    /// One active branch entry on a <see cref="VmThread"/>'s frame stack — "loop counters, branch returns"
    /// (TDD §6.3). Pushed by a C-block op on first entry into a branch; popped by that same op once it decides
    /// to stop looping. <see cref="YieldedThisLap"/> backs the loop-yield rule (TDD §6.4): the VM forces a
    /// frame yield at the branch's exit whenever the lap that just finished never yielded on its own.
    /// </summary>
    public struct Frame
    {
        public int OwnerPc;
        public int ExitPc;
        public int Counter;
        public bool YieldedThisLap;

        /// <summary>
        /// True for a frame that may re-enter its branch (repeat/repeat_until/repeat_forever); false for a
        /// one-shot frame used only to skip a sibling branch (if_else's taken arm). The loop-yield rule only
        /// forces a yield at the wrap for loop frames — forcing one on every if/if_else would add a hidden
        /// per-frame delay to the single most common block in the catalog.
        /// </summary>
        public bool IsLoop;

        public Frame(int ownerPc, int exitPc, int counter, bool isLoop)
        {
            OwnerPc = ownerPc;
            ExitPc = exitPc;
            Counter = counter;
            IsLoop = isLoop;
            YieldedThisLap = false;
        }
    }
}
