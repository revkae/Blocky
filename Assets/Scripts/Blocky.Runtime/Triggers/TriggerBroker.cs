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
            OnPlayClicked?.Invoke();
        }

        /// <summary>Call when a new play session starts, so <see cref="FirePlayClicked"/> can fire again.</summary>
        public void ResetPlaySession() => _playClickedFired = false;

        /// <summary>Whether "when Play clicked" has already fired this session (Step back saves it, to re-arm it when stepping back past the start).</summary>
        public bool PlayClickedFired => _playClickedFired;

        /// <summary>The in-game "Go" button (TDD-equivalent of Scratch's green flag) — click it as many times as you like, each fires this again.</summary>
        public event Action OnGoClicked;

        public void FireGoClicked() => OnGoClicked?.Invoke();

        /// <summary>
        /// "Start every script in the scene, whatever hat it has" — the channel Step forward uses. A learner walking
        /// a program block by block cannot produce a key press, a collision or a look while the scene is frozen
        /// between steps, so without this only "when Play clicked" and "when Go clicked" scripts could ever be
        /// stepped. Listeners leave a script that is already running exactly where it is.
        /// </summary>
        public event Action OnStepAll;

        public void FireStepAll() => OnStepAll?.Invoke();

        /// <summary>
        /// Scratch's broadcast: a named message any script can send and any number of scripts can listen for.
        /// Listeners match the name themselves (case-insensitively), the way key listeners match their key —
        /// the broker stays a dumb channel and never has to know which messages a program uses.
        /// </summary>
        public event Action<string> OnBroadcast;

        public void Broadcast(string message) => OnBroadcast?.Invoke(message ?? string.Empty);

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

            var mouse = Mouse.current;
            if (mouse == null) return;

            var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out var hit)) OnClicked?.Invoke(hit.collider.gameObject);
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

        /// <summary>Forwarded by a <see cref="BlockCollisionRelay"/>, never raised by a block itself (TDD §9).</summary>
        public event Action<GameObject, Collision> OnCollided;

        public void RaiseCollided(GameObject source, Collision collision) => OnCollided?.Invoke(source, collision);

        private readonly Dictionary<GameObject, (float thresholdDegrees, bool wasLookedAt)> _lookedAtListeners = new();

        /// <summary>Rising edge only (TDD §9).</summary>
        public event Action<GameObject> OnLookedAt;

        public void RegisterLookedAt(GameObject target, float thresholdDegrees) =>
            _lookedAtListeners[target] = (thresholdDegrees, false);

        public void UnregisterLookedAt(GameObject target) => _lookedAtListeners.Remove(target);

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
