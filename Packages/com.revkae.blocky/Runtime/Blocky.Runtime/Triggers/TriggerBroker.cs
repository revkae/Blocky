using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blocky.Runtime.Triggers
{
    /// <summary>
    /// Typed channels for the v1 trigger catalog (TDD §6.7). Runners subscribe on enable and unsubscribe on
    /// disable. Polling methods are called once per frame by a single driver (<see cref="BlockyRuntimeTicker"/>)
    /// — one keyboard poll and one raycast serve every listener, never one per object (TDD §6.7's WhenLookedAt note).
    /// </summary>
    public sealed class TriggerBroker
    {
        private bool _playClickedFired;

        /// <summary>Fires once per play session (TDD §9).</summary>
        public event Action OnPlayClicked;

        public void FirePlayClicked()
        {
            if (_playClickedFired) return;
            _playClickedFired = true;
            InvokeScoped(OnPlayClicked, null);
        }

        /// <summary>Call when a new play session starts, so <see cref="FirePlayClicked"/> can fire again.</summary>
        public void ResetPlaySession() => _playClickedFired = false;

        /// <summary>Whether "when Play clicked" has already fired this session (Step back saves it, to re-arm it when stepping back past the start).</summary>
        public bool PlayClickedFired => _playClickedFired;

        /// <summary>The in-game "Go" button (TDD-equivalent of Scratch's green flag) — click it as many times as you like, each fires this again.</summary>
        public event Action OnGoClicked;

        /// <summary>Fires "when Go clicked"; with <paramref name="only"/>, only that object's scripts react.</summary>
        public void FireGoClicked(GameObject only = null) => InvokeScoped(OnGoClicked, only);

        /// <summary>
        /// Fires "when Play clicked" for one object only, without touching the once-per-session flag — the
        /// scene-wide <see cref="FirePlayClicked"/> stays armed for the other objects.
        /// </summary>
        public void FirePlayClickedFor(GameObject only) => InvokeScoped(OnPlayClicked, only);

        private GameObject _scope;

        /// <summary>Whether <paramref name="go"/> should react to the Play/Go event being fired: always, unless the fire was aimed at one object.</summary>
        public bool InScope(GameObject go) => _scope == null || _scope == go;

        private void InvokeScoped(Action handlers, GameObject only)
        {
            _scope = only;
            try { handlers?.Invoke(); }
            finally { _scope = null; }
        }

        /// <summary>
        /// "Start every script in the scene, whatever hat it has" — the channel Step forward uses. A learner walking
        /// a program block by block cannot produce a key press, a collision or a look while the scene is frozen
        /// between steps, so without this only "when Play clicked" and "when Go clicked" scripts could ever be
        /// stepped. Listeners leave a script that is already running exactly where it is. The argument is the one
        /// object to start scripts on, or null for every object; listeners on any other object ignore it.
        /// </summary>
        public event Action<GameObject> OnStepAll;

        public void FireStepAll(GameObject only = null) => OnStepAll?.Invoke(only);

        /// <summary>
        /// Scratch's broadcast: a named message any script can send and any number of scripts can listen for.
        /// Listeners match the name themselves (case-insensitively), the way key listeners match their key —
        /// the broker stays a dumb channel and never has to know which messages a program uses.
        /// </summary>
        public event Action<string> OnBroadcast;

        public void Broadcast(string message) => OnBroadcast?.Invoke(message ?? string.Empty);

        /// <summary>
        /// The level is complete — a <c>level complete</c> block ran (the object is its), or game code called
        /// <see cref="BlockyEvents.CompleteLevel"/>. Every <c>when level complete</c> script starts, and
        /// <see cref="BlockyEvents.LevelCompleted"/> tells game code.
        /// </summary>
        public event Action<GameObject> OnLevelCompleted;

        public void RaiseLevelCompleted(GameObject by) => OnLevelCompleted?.Invoke(by);

        private bool _pointerDownLastPoll;

        /// <summary>Rising edge of a click on an object in the scene — the object the ray hit, never a UI press.</summary>
        public event Action<GameObject> OnClicked;

        /// <summary>
        /// One raycast per frame for every <c>event.when_clicked</c> listener, on the press edge only. A press on the
        /// Blocky editor is not a press in the game (<see cref="BlockyInput"/> filters it), so dragging a block out of
        /// the palette can never fire a script.
        /// </summary>
        public void PollClicked(Camera camera)
        {
            var isDown = BlockyInput.IsMouseDown;
            var wasDown = _pointerDownLastPoll;
            _pointerDownLastPoll = isDown;

            if (!isDown || wasDown || camera == null) return;
            if (!BlockyInput.TryGetPointerPosition(out var position)) return; // mouse, pen or finger — the one just pressed

            var clicked = BlockyPicking.ObjectAt(camera, position); // a 3D or 2D collider, or a sprite without one
            if (clicked != null) OnClicked?.Invoke(clicked);
        }

        /// <summary>Test seam: fires <see cref="OnClicked"/> for <paramref name="target"/> without a camera or a mouse.</summary>
        public void RaiseClicked(GameObject target) => OnClicked?.Invoke(target);

        /// <summary>
        /// A clone has just been made and is ready to run — Scratch's <c>when I start as a clone</c>. Every runner
        /// hears it and checks whether the clone is itself, the same way the click and collision channels work.
        /// </summary>
        public event Action<GameObject> OnCloneStarted;

        public void RaiseCloneStarted(GameObject clone) => OnCloneStarted?.Invoke(clone);

        private readonly HashSet<Key> _keysDownLastPoll = new();

        /// <summary>Rising edge only, filtered by key id downstream by each listener (TDD §9).</summary>
        public event Action<Key> OnKeyPressed;

        /// <summary>One poll per frame for every <c>event.when_key_pressed</c> listener.</summary>
        public void PollKeyboard(Keyboard keyboard)
        {
            if (keyboard == null) return;

            foreach (var control in keyboard.allKeys)
            {
                var key = control.keyCode;
                var isDown = control.isPressed;
                var wasDown = _keysDownLastPoll.Contains(key);

                if (isDown && !wasDown) OnKeyPressed?.Invoke(key);

                if (isDown) _keysDownLastPoll.Add(key);
                else _keysDownLastPoll.Remove(key);
            }
        }

        /// <summary>
        /// <paramref name="source"/> bumped into the other object — forwarded by a <see cref="BlockCollisionRelay"/>
        /// from 3D or 2D physics, never raised by a block itself (TDD §9). The other object, not the physics engine's
        /// collision record, so one channel serves both engines.
        /// </summary>
        public event Action<GameObject, GameObject> OnCollided;

        public void RaiseCollided(GameObject source, GameObject other) => OnCollided?.Invoke(source, other);

        private readonly Dictionary<GameObject, (float thresholdDegrees, bool wasLookedAt)> _lookedAtListeners = new();

        /// <summary>Rising edge only (TDD §9).</summary>
        public event Action<GameObject> OnLookedAt;

        public void RegisterLookedAt(GameObject target, float thresholdDegrees) =>
            _lookedAtListeners[target] = (thresholdDegrees, false);

        public void UnregisterLookedAt(GameObject target) => _lookedAtListeners.Remove(target);

        /// <summary>
        /// Fires "when looked at" for exactly this object, as <see cref="PollLookedAt"/> does on the rising edge. For a
        /// game with its own gaze (a reticle raycast with a dwell timer): turn
        /// <see cref="BlockyRuntimeTicker.PollLookedAt"/> off and call this instead. The thresholds given to
        /// <see cref="RegisterLookedAt"/> are simply unused then.
        /// </summary>
        public void RaiseLookedAt(GameObject target) => OnLookedAt?.Invoke(target);

        /// <summary>One shared raycast-equivalent (angle check) per frame, compared against every listener (TDD §6.7).</summary>
        public void PollLookedAt(Camera camera)
        {
            if (camera == null || _lookedAtListeners.Count == 0) return;

            // Snapshot the keys first: writing an update back into the dictionary while foreach-ing it directly
            // throws "Collection was modified", even for an existing key.
            List<GameObject> destroyed = null;
            foreach (var target in new List<GameObject>(_lookedAtListeners.Keys))
            {
                if (target == null) { (destroyed ??= new List<GameObject>()).Add(target); continue; }

                var (thresholdDegrees, wasLookedAt) = _lookedAtListeners[target];
                var toTarget = (target.transform.position - camera.transform.position).normalized;
                var angle = Vector3.Angle(camera.transform.forward, toTarget);
                var isLookedAt = angle <= thresholdDegrees;

                if (isLookedAt && !wasLookedAt) OnLookedAt?.Invoke(target);
                _lookedAtListeners[target] = (thresholdDegrees, isLookedAt);
            }

            if (destroyed == null) return;
            foreach (var target in destroyed) _lookedAtListeners.Remove(target);
        }
    }
}
