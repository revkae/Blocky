using UnityEngine;

// Blocky resets this static itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime.Persistence
{
    /// <summary>
    /// Which <see cref="IProgramStorage"/> the in-game editor saves to and runners load from. A game sets its own as
    /// it starts — before its programmed objects wake, or they look in the files first.
    /// </summary>
    public static class BlockyStorage
    {
        private static IProgramStorage _current;

        /// <summary>The storage in use. <see cref="FileProgramStorage.Instance"/> until a game sets its own, and again after setting null.</summary>
        public static IProgramStorage Current
        {
            get => _current ?? FileProgramStorage.Instance;
            set => _current = value;
        }

        /// <summary>Domain reload is off (ADR-014): a storage set in one Play session would still be there, with its game's objects gone, in the next.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession() => _current = null;
    }
}
