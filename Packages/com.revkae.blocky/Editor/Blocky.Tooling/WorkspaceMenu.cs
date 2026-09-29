using Blocky.Game;
using UnityEditor;
using UnityEngine;

namespace Blocky.Tooling
{
    /// <summary>
    /// <c>GameObject › Blocky › Workspace</c>: puts the in-game workspace in the open scene, already wired — the
    /// package's <c>Blocky Workspace</c> prefab. Nothing else is needed: the first runner or the workspace adds the
    /// <c>BlockyRuntimeTicker</c> itself. One per scene, since every workspace answers the same toggle key.
    /// </summary>
    internal static class WorkspaceMenu
    {
        [MenuItem("GameObject/Blocky/Workspace", false, 10)]
        private static void CreateWorkspace(MenuCommand command)
        {
            var existing = Object.FindAnyObjectByType<BlockyInGamePanel>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Debug.Log($"Blocky: this scene already has a workspace, \"{existing.name}\".", existing);
                Selection.activeObject = existing.gameObject;
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockyPaths.WorkspacePrefab);
            if (prefab == null)
            {
                Debug.LogError($"Blocky: the workspace prefab is missing from {BlockyPaths.WorkspacePrefab} — reinstall the package.");
                return;
            }

            var workspace = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObjectUtility.SetParentAndAlign(workspace, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(workspace, "Create Blocky Workspace");
            Selection.activeObject = workspace;
        }
    }
}
