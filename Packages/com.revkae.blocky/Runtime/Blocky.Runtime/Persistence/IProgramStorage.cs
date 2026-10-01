using Blocky.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blocky.Runtime.Persistence
{
    /// <summary>
    /// Where the programs the in-game editor writes are kept, and where a runner looks for its object's saved
    /// program. Blocky's own keeps one file per object (<see cref="FileProgramStorage"/>, ADR-031); a game that keeps
    /// programs somewhere of its own — inside its world's save data, on a server — implements this and hands it to
    /// <see cref="BlockyStorage.Current"/> (ADR-041).
    /// </summary>
    public interface IProgramStorage
    {
        /// <summary>Cheap pre-check: can anything be saved for objects in this scene? False, and runners never ask per object.</summary>
        bool HasSavesFor(Scene scene);

        /// <summary>Whether a program is saved for this object — asked to give saved objects a runner at scene start.</summary>
        bool Exists(GameObject owner);

        /// <summary>
        /// <see cref="SavedProgramStatus.None"/>: nothing saved. <see cref="SavedProgramStatus.Unreadable"/>: something
        /// is there but can't be used — it must not be overwritten by the next save.
        /// </summary>
        SavedProgramStatus TryLoad(GameObject owner, out ObjectProgram program);

        /// <summary>Never throws: a save that fails must not undo the edit that caused it. False, with the reason, when nothing was written.</summary>
        bool TrySave(GameObject owner, ObjectProgram program, out string problem);
    }
}
