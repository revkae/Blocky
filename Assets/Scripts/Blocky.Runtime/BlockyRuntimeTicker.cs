using UnityEngine;
using UnityEngine.InputSystem;

namespace Blocky.Runtime
{
    /// <summary>
    /// The single MonoBehaviour that drives the whole runtime per frame (TDD §6.5): one scheduler tick, one
    /// keyboard poll, one look-at check, one click raycast, shared by every <c>ObjectProgramRunner</c> in the scene. Add exactly
    /// one of these to a scene that uses Blocky at runtime.
    /// </summary>
    public sealed class BlockyRuntimeTicker : MonoBehaviour
    {
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
