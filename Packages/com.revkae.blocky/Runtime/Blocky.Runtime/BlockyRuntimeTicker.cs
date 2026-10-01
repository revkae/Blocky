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
    /// A game that starts scripts and reads input itself puts one in its scene with the switches below turned off
    /// (ADR-041); the scheduler ticks whatever they say.
    /// </summary>
    public sealed class BlockyRuntimeTicker : MonoBehaviour
    {
        /// <summary>The name of the object <see cref="EnsureExists"/> adds.</summary>
        public const string AddedObjectName = "Blocky Runtime";

        [Tooltip("Start every \"when Play clicked\" script when the scene starts. Off: the game starts them itself (Playback.Go, or the trigger broker).")]
        [SerializeField] private bool firePlayClickedOnStart = true;

        [Tooltip("At scene start, give a runner back to every object that has a saved program but no runner. Off: the game hands out programs itself.")]
        [SerializeField] private bool attachSavedPrograms = true;

        [Tooltip("Read the keyboard for \"when key pressed\".")]
        [SerializeField] private bool pollKeyboard = true;

        [Tooltip("Check which objects the main camera faces, for \"when looked at\". Off: the game decides, and calls TriggerBroker.RaiseLookedAt.")]
        [SerializeField] private bool pollLookedAt = true;

        [Tooltip("Raycast on a press, for \"when clicked\". Off: the game decides, and calls TriggerBroker.RaiseClicked.")]
        [SerializeField] private bool pollClicked = true;

        /// <summary>Whether every "when Play clicked" script starts when the scene does. Read once, in <c>Start</c>.</summary>
        public bool FirePlayClickedOnStart
        {
            get => firePlayClickedOnStart;
            set => firePlayClickedOnStart = value;
        }

        /// <summary>Whether objects with a saved program but no runner get one back at scene start (ADR-031). Read once, in <c>Start</c>.</summary>
        public bool AttachSavedPrograms
        {
            get => attachSavedPrograms;
            set => attachSavedPrograms = value;
        }

        /// <summary>Whether the keyboard is read each frame for "when key pressed".</summary>
        public bool PollKeyboard
        {
            get => pollKeyboard;
            set => pollKeyboard = value;
        }

        /// <summary>Whether "when looked at" is decided here, by the angle to the main camera. A game with its own gaze turns it off and calls <c>TriggerBroker.RaiseLookedAt</c>.</summary>
        public bool PollLookedAt
        {
            get => pollLookedAt;
            set => pollLookedAt = value;
        }

        /// <summary>Whether "when clicked" is decided here, by a raycast on each press.</summary>
        public bool PollClicked
        {
            get => pollClicked;
            set => pollClicked = value;
        }

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

        private void Start() => Begin();

        /// <summary>
        /// Before the first tick, and before "when Play clicked": an object first programmed in the game got its runner
        /// from the in-game editor while the game ran, so it has none in the scene. It gets one back here, and runs its
        /// saved program (ADR-031). In <c>Start</c> rather than <c>Awake</c>, so a ticker added from code can be
        /// configured between <c>AddComponent</c> and its first frame.
        /// </summary>
        internal void Begin()
        {
            if (attachSavedPrograms)
                for (var i = 0; i < SceneManager.sceneCount; i++)
                    ObjectProgramRunner.AttachToSavedObjects(SceneManager.GetSceneAt(i));

            if (firePlayClickedOnStart) BlockyRuntime.Triggers.FirePlayClicked();
        }

        private void Update()
        {
            Poll(Keyboard.current, pollLookedAt || pollClicked ? Camera.main : null);
            BlockyRuntime.Scheduler.Tick(Time.deltaTime);
        }

        /// <summary>One frame's input, for the channels that are switched on.</summary>
        internal void Poll(Keyboard keyboard, Camera camera)
        {
            var triggers = BlockyRuntime.Triggers;
            if (pollKeyboard) triggers.PollKeyboard(keyboard);
            if (camera == null) return;
            if (pollLookedAt) triggers.PollLookedAt(camera);
            if (pollClicked) triggers.PollClicked(camera);
        }
    }
}
