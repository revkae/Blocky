using System.IO;
using Blocky.Runtime.Persistence;
using UnityEditor;
using UnityEngine;

namespace Blocky.Tooling
{
    /// <summary>
    /// Programs saved from the in-game editor win over the scene's programs when the game starts (ADR-031) — in the
    /// Editor's Play mode too. So a program changed here, in the Unity Editor, doesn't run while an in-game save for
    /// that object exists. These menu items show where the saves are and clear them.
    /// </summary>
    internal static class InGameSavesMenu
    {
        [MenuItem("Blocky/In-Game Saves/Show Folder")]
        private static void ShowFolder()
        {
            var root = RuntimeProgramStorage.Root;
            if (Directory.Exists(root)) EditorUtility.RevealInFinder(root);
            else EditorUtility.DisplayDialog("Blocky", $"Nothing has been saved from the in-game editor yet. Saves will go to:\n\n{root}", "OK");
        }

        [MenuItem("Blocky/In-Game Saves/Delete All…")]
        private static void DeleteAll()
        {
            var root = RuntimeProgramStorage.Root;
            if (!Directory.Exists(root))
            {
                EditorUtility.DisplayDialog("Blocky", "Nothing has been saved from the in-game editor yet.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete in-game saves?",
                    $"Every program saved from the in-game editor on this computer will be deleted, and each object will run its scene program again. This can't be undone.\n\n{root}",
                    "Delete", "Cancel"))
                return;

            Directory.Delete(root, true);
            Debug.Log($"Blocky: deleted the in-game saves in {root}");
        }
    }
}
