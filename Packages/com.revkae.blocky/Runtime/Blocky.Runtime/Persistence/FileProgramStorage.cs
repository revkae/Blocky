using Blocky.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

// The static here is a fixed value that never changes, so there is nothing to reset between Play sessions.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime.Persistence
{
    /// <summary>
    /// Blocky's own storage, and the default: one JSON file per object under the player's save folder, keyed by scene
    /// and hierarchy path (ADR-031). The files, their keys and their safety rules are <see cref="RuntimeProgramStorage"/>'s;
    /// this only works out an object's key.
    /// </summary>
    public sealed class FileProgramStorage : IProgramStorage
    {
        public static readonly FileProgramStorage Instance = new();

        private FileProgramStorage() { }

        public bool HasSavesFor(Scene scene) => RuntimeProgramStorage.HasSavesFor(scene);

        public bool Exists(GameObject owner) => owner != null && RuntimeProgramStorage.Exists(RuntimeProgramStorage.KeyFor(owner));

        public SavedProgramStatus TryLoad(GameObject owner, out ObjectProgram program)
        {
            program = null;
            return owner == null ? SavedProgramStatus.None : RuntimeProgramStorage.TryLoad(RuntimeProgramStorage.KeyFor(owner), out program);
        }

        public bool TrySave(GameObject owner, ObjectProgram program, out string problem)
        {
            if (owner == null)
            {
                problem = "The object the program belongs to is gone.";
                return false;
            }

            return RuntimeProgramStorage.TrySave(RuntimeProgramStorage.KeyFor(owner), program, out problem);
        }
    }
}
