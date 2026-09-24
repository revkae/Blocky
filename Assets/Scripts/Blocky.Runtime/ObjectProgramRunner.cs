using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blocky.Runtime
{
    /// <summary>
    /// The MonoBehaviour that makes one <see cref="GameObject"/> run its own compiled program (TDD §3, §6.4,
    /// §6.7 — Milestone 6's "first end-to-end authored-and-run behaviour"). Compiles on enable, subscribes to
    /// the shared <see cref="TriggerBroker"/> per stack, and applies each trigger's <see cref="RetriggerPolicy"/>.
    /// Disabling halts every thread it started; re-enabling does not resume them (TDD §6.4) — triggers must fire again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectProgramRunner : MonoBehaviour
    {
        [SerializeField] private BlockProgramAsset programAsset;

        private CompiledProgram _compiled;
        private bool _initialized;
        private readonly List<(BlockStack stack, BlockDefinition trigger, int entryPc)> _stacks = new();
        private readonly List<Action> _unsubscribe = new();

        /// <summary>Assign before enabling — Unity runs <c>OnEnable</c> the instant an inactive object with this component becomes active.</summary>
        public void SetProgramAsset(BlockProgramAsset asset) => programAsset = asset;

        /// <summary>The asset this runner currently loads from, or null. Runtime-safe read (no <c>SerializedObject</c> needed).</summary>
        public BlockProgramAsset ProgramAsset => programAsset;

        private void OnEnable() => Initialize();

        private void OnDisable() => Shutdown();

        /// <summary>
        /// Compiles the program and subscribes to triggers. Called by <c>OnEnable</c> for normal use; exposed
        /// publicly as a deterministic seam for tests, since edit-mode <c>OnEnable</c> timing via
        /// <c>GameObject.SetActive</c> is not guaranteed to run synchronously inside the test runner.
        /// </summary>
        public void Initialize()
        {
            // Idempotent: OnEnable and an explicit call race in edit mode, and a clone is initialised by hand so
            // that it is subscribed before its "when I start as a clone" hat fires. Whichever runs first wins;
            // subscribing twice would fire every hat twice. Shutdown clears the flag, so the editor's
            // Shutdown-then-Initialize after an edit still rebuilds everything.
            if (_initialized) return;
            _initialized = true;

            BlockyRuntime.World.Capture(gameObject); // first start only: where this object began, for the editor's Reset
            BlockyRuntime.Objects.Register(gameObject);  // so another object's "distance to [me]" never has to search the scene

            var registry = BlockyRuntime.Registry;
            var program = programAsset != null ? programAsset.Load() : new ObjectProgram();
            ProgramUpgrades.UpgradeCheckboxConditions(program, registry); // assets saved before condition blocks existed
            var result = ProgramCompiler.Link(program, registry);
            _compiled = result.Program;

            _stacks.Clear();
            for (var i = 0; i < program.stacks.Length; i++)
            {
                var stack = program.stacks[i];
                if (ProgramQuery.IsLoose(stack)) continue; // blocks lying loose on the table never run
                var entryPc = _compiled.StackEntryPoints[i];
                var triggerDef = registry.Find(stack.triggerBlockType);
                if (triggerDef == null || entryPc < 0) continue; // unknown trigger type, or this stack failed to compile (TDD §10.2)
                _stacks.Add((stack, triggerDef, entryPc));
            }

            if (GetComponent<Collider>() != null && GetComponent<BlockCollisionRelay>() == null)
                gameObject.AddComponent<BlockCollisionRelay>();

            SubscribeTriggers();
        }

        /// <summary>Halts every thread this runner started and unsubscribes from triggers. See <see cref="Initialize"/> for why this is public.</summary>
        public void Shutdown()
        {
            _initialized = false;
            foreach (var unsubscribe in _unsubscribe) unsubscribe();
            _unsubscribe.Clear();
            BlockyRuntime.Objects.Unregister(gameObject);

            if (_compiled != null) BlockyRuntime.Scheduler.StopWhere(gameObject, _compiled);
        }

        private void SubscribeTriggers()
        {
            var broker = BlockyRuntime.Triggers;

            foreach (var (stack, triggerDef, entryPc) in _stacks)
            {
                // Any stack can also be started by hand from the run bar's Step forward, whatever hat it has.
                void StepHandler() => FireForStep(entryPc);
                broker.OnStepAll += StepHandler;
                _unsubscribe.Add(() => broker.OnStepAll -= StepHandler);

                switch (triggerDef.blockType)
                {
                    case "event.when_play_clicked":
                    {
                        void Handler() => Fire(triggerDef, entryPc);
                        broker.OnPlayClicked += Handler;
                        _unsubscribe.Add(() => broker.OnPlayClicked -= Handler);
                        break;
                    }
                    case "event.when_go_clicked":
                    {
                        void Handler() => Fire(triggerDef, entryPc);
                        broker.OnGoClicked += Handler;
                        _unsubscribe.Add(() => broker.OnGoClicked -= Handler);
                        break;
                    }
                    case "event.when_key_pressed":
                    {
                        void Handler(Key key)
                        {
                            if (MatchesKey(stack, key)) Fire(triggerDef, entryPc);
                        }
                        broker.OnKeyPressed += Handler;
                        _unsubscribe.Add(() => broker.OnKeyPressed -= Handler);
                        break;
                    }
                    case "event.when_collided":
                    {
                        void Handler(GameObject source, Collision collision)
                        {
                            if (source == gameObject && MatchesTag(stack, collision)) Fire(triggerDef, entryPc);
                        }
                        broker.OnCollided += Handler;
                        _unsubscribe.Add(() => broker.OnCollided -= Handler);
                        break;
                    }
                    case "event.when_clicked":
                    {
                        void Handler(GameObject clicked)
                        {
                            // The ray hits whichever collider is in front, which may be a child of the programmed object.
                            if (clicked == gameObject || (clicked != null && clicked.transform.IsChildOf(transform))) Fire(triggerDef, entryPc);
                        }
                        broker.OnClicked += Handler;
                        _unsubscribe.Add(() => broker.OnClicked -= Handler);
                        break;
                    }
                    case "event.when_i_start_as_a_clone":
                    {
                        void Handler(GameObject clone)
                        {
                            if (clone == gameObject) Fire(triggerDef, entryPc);
                        }
                        broker.OnCloneStarted += Handler;
                        _unsubscribe.Add(() => broker.OnCloneStarted -= Handler);
                        break;
                    }
                    case "event.when_broadcast_received":
                    {
                        void Handler(string message)
                        {
                            if (MatchesMessage(stack, message)) Fire(triggerDef, entryPc);
                        }
                        broker.OnBroadcast += Handler;
                        _unsubscribe.Add(() => broker.OnBroadcast -= Handler);
                        break;
                    }
                    case "event.when_looked_at":
                    {
                        var thresholdParam = Array.Find(stack.triggerParameters, p => p.key == "angle_threshold");
                        broker.RegisterLookedAt(gameObject, thresholdParam?.number ?? 15f);

                        void Handler(GameObject target)
                        {
                            if (target == gameObject) Fire(triggerDef, entryPc);
                        }
                        broker.OnLookedAt += Handler;
                        _unsubscribe.Add(() =>
                        {
                            broker.OnLookedAt -= Handler;
                            broker.UnregisterLookedAt(gameObject);
                        });
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Applies <see cref="RetriggerPolicy"/> then starts a thread (TDD §6.4). RestartOnRetrigger kills the
        /// existing thread and starts a fresh one rather than resetting its pc in place — equivalent for v1
        /// since there is no thread pooling yet to make in-place reset actually cheaper (deferred to Milestone 7).
        /// The stack's current thread is looked up in the scheduler rather than remembered here, so a thread brought
        /// back by Step back still counts as the one running.
        /// </summary>
        private void Fire(BlockDefinition triggerDef, int entryPc)
        {
            var scheduler = BlockyRuntime.Scheduler;

            if (triggerDef.retrigger == RetriggerPolicy.AllowConcurrent)
            {
                scheduler.Start(_compiled, gameObject, entryPc);
                return;
            }

            var existing = scheduler.FindLive(gameObject, _compiled, entryPc);
            if (triggerDef.retrigger == RetriggerPolicy.IgnoreWhileRunning && existing != null) return;
            if (triggerDef.retrigger == RetriggerPolicy.RestartOnRetrigger && existing != null) existing.State = ThreadState.Done;

            scheduler.Start(_compiled, gameObject, entryPc);
        }

        /// <summary>
        /// Starts this stack for a Step forward, unless it is already running. Deliberately ignores the trigger's
        /// <see cref="RetriggerPolicy"/>: every event hat is <c>RestartOnRetrigger</c>, and restarting a script the
        /// learner is halfway through walking, one block per press, would be the opposite of stepping.
        /// </summary>
        private void FireForStep(int entryPc)
        {
            var scheduler = BlockyRuntime.Scheduler;
            if (scheduler.FindLive(gameObject, _compiled, entryPc) != null) return;
            scheduler.Start(_compiled, gameObject, entryPc);
        }

        private static bool MatchesKey(BlockStack stack, Key firedKey)
        {
            var keyParam = Array.Find(stack.triggerParameters, p => p.key == "key");
            return keyParam != null && Enum.TryParse<Key>(keyParam.text, true, out var wantedKey) && wantedKey == firedKey;
        }

        /// <summary>Message names are compared case- and whitespace-insensitively: a learner typing "Jump" and "jump " means one message.</summary>
        private static bool MatchesMessage(BlockStack stack, string message)
        {
            var wanted = Array.Find(stack.triggerParameters, p => p.key == "message")?.text;
            return string.Equals((wanted ?? string.Empty).Trim(), (message ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesTag(BlockStack stack, Collision collision)
        {
            var tagParam = Array.Find(stack.triggerParameters, p => p.key == "tag_filter");
            return tagParam == null || string.IsNullOrEmpty(tagParam.text) || collision.gameObject.CompareTag(tagParam.text);
        }
    }
}
