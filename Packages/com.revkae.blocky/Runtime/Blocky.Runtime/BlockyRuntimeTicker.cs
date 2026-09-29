using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Blocky.Runtime
{
    /// <summary>
    /// The single MonoBehaviour that drives the whole runtime per frame (TDD §6.5): one scheduler tick, one
    /// keyboard poll, one look-at check, one click raycast, shared by every <c>ObjectProgramRunner</c> in the scene.
    /// A scene that uses Blocky needs exactly one. Put it where you like; a scene without one gets one from the first
    /// runner to start in Play mode, or from the in-game editor (<see cref="EnsureExists"/>) — without it, no block runs.
    /// </summary>
    public sealed class BlockyRuntimeTicker : MonoBehaviour
    {
        /// <summary>The name of the object <see cref="EnsureExists"/> adds.</summary>
        public const string AddedObjectName = "Blocky Runtime";

        /// <summary>
        /// Adds a ticker, on a new object in the active scene, when none is loaded — and says so in the console. Play
        /// mode only: tests step the scheduler themselves.
        /// </summary>
        public static void EnsureExists()
        {
            if (!Application.isPlaying || FindAnyObjectByType<BlockyRuntimeTicker>(FindObjectsInactive.Include) != null) return;

            new GameObject(AddedObjectName).AddComponent<BlockyRuntimeTicker>();
            Debug.Log($"Blocky: no {nameof(BlockyRuntimeTicker)} was in the scene, and no block runs without one, so one was added " +
                      $"on a new '{AddedObjectName}' object. Add one to the scene yourself to choose where it lives.");
        }

        /// <summary>
        /// Before any script starts: an object first programmed in the game got its runner from the in-game editor
        /// while the game ran, so it has none in the scene. It gets one back here, and runs its saved program (ADR-031).
        /// </summary>
        private void Awake()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
                ObjectProgramRunner.AttachToSavedObjects(SceneManager.GetSceneAt(i));
        }

        private void Start() => BlockyRuntime.Triggers.FirePlayClicked();

        private void Update()
        {
            BlockyRuntime.Triggers.PollKeyboard(Keyboard.current);
            if (Camera.main != null)
            {
                BlockyRuntime.Triggers.PollLookedAt(Camera.main);
                BlockyRuntime.Triggers.PollClicked(Camera.main);
            }
            BlockyRuntime.Scheduler.Tick(Time.deltaTime);
        }
    }
}
