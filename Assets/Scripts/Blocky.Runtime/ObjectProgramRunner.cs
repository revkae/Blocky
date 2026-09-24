using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Blocky.Runtime
{
    /// <summary>
    /// The MonoBehaviour that makes one <see cref="GameObject"/> run its own compiled program (TDD §3, §6.4,
    /// §6.7 — Milestone 6's "first end-to-end authored-and-run behaviour"). Compiles on enable, subscribes to
    /// the shared <see cref="TriggerBroker"/> per stack, and applies each trigger's <see cref="RetriggerPolicy"/>.
    /// Disabling halts every thread it started; re-enabling does not resume them (TDD §6.4) — triggers must fire again.
    /// The program it runs is the one the in-game editor saved for this object, when there is one (ADR-031), else
    /// the one it was given in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectProgramRunner : MonoBehaviour
    {
        [SerializeField] private BlockProgramAsset programAsset;

        private CompiledProgram _compiled;
        private bool _initialized;
        private bool _programChosen; // the save has been looked for, or code handed a program over — either way, don't look again
        private SavedProgramStatus _savedProgram;
        private readonly List<(BlockStack stack, BlockDefinition trigger, int entryPc)> _stacks = new();
        private readonly List<Action> _unsubscribe = new();

        /// <summary>
        /// Assign before enabling — Unity runs <c>OnEnable</c> the instant an inactive object with this component
        /// becomes active. A program handed over this way wins over one saved by the in-game editor.
        /// </summary>
        public void SetProgramAsset(BlockProgramAsset asset)
        {
            programAsset = asset;
            _programChosen = true;
            _savedProgram = SavedProgramStatus.None; // whatever the save held, this is what runs now
        }

        /// <summary>The asset this runner currently loads from, or null. Runtime-safe read (no <c>SerializedObject</c> needed).</summary>
        public BlockProgramAsset ProgramAsset => programAsset;

        /// <summary>
        /// What the in-game editor had saved for this object when the runner looked — <see cref="SavedProgramStatus.None"/>
        /// before it looks, and again once code hands it a program (<see cref="SetProgramAsset"/>), which then runs instead.
        /// </summary>
        public SavedProgramStatus SavedProgram => _savedProgram;

        /// <summary>
        /// The program this object runs — the same one it compiles on <see cref="Initialize"/>, so an editor opening it
        /// shows exactly what runs: the program the in-game editor saved for it in an earlier session when there is one,
        /// else the one it was given in the scene. Empty when it has neither.
        /// </summary>
        public ObjectProgram LoadProgram()
        {
            UseSavedProgram();
            return programAsset != null ? programAsset.Load() : new ObjectProgram();
        }

        /// <summary>
        /// The first time the program is needed, looks for one saved by the in-game editor and, when there is one,
        /// runs it instead of the scene's. It becomes this runner's asset, so a clone made later copies it and runs
        /// the same program. A clone looks too, under its own key, and finds nothing: saves belong to scene objects.
        /// </summary>
        private void UseSavedProgram()
        {
            if (_programChosen) return;
            _programChosen = true;
            if (!RuntimeProgramStorage.HasSavesFor(gameObject.scene)) return;

            var key = RuntimeProgramStorage.KeyFor(gameObject);
            _savedProgram = RuntimeProgramStorage.TryLoad(key, out var saved);
            if (_savedProgram != SavedProgramStatus.Loaded) return;

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.name = key;
            asset.Save(saved);
            programAsset = asset;
        }

        /// <summary>
        /// Gives a runner back to every object in <paramref name="scene"/> that the in-game editor saved a program
        /// for but that has none of its own — an object first programmed in the game, where the editor added the runner
        /// while it ran. Objects that already have a runner find their save themselves. Returns how many were added.
        /// </summary>
        public static int AttachToSavedObjects(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !RuntimeProgramStorage.HasSavesFor(scene)) return 0;

            var roots = scene.GetRootGameObjects();
            var transforms = new Transform[roots.Length];
            for (var i = 0; i < roots.Length; i++) transforms[i] = roots[i].transform;
            Array.Sort(transforms, (a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
            return AttachUnder(transforms, RuntimeProgramStorage.SceneKey(scene));
        }

        /// <summary>
        /// Walks one level of siblings, in hierarchy order, building each key the way
        /// <see cref="RuntimeProgramStorage.KeyFor"/> does — but once for the whole scene rather than per object.
        /// </summary>
        private static int AttachUnder(Transform[] siblings, string parentKey)
        {
            var added = 0;
            var seen = new Dictionary<string, int>(RuntimeProgramStorage.NameComparer);
            foreach (var t in siblings)
            {
                seen.TryGetValue(t.name, out var sameNameIndex);
                seen[t.name] = sameNameIndex + 1;
                var key = RuntimeProgramStorage.ChildKey(parentKey, t.name, sameNameIndex);

                if (t.GetComponent<ObjectProgramRunner>() == null && RuntimeProgramStorage.Exists(key))
                {
                    t.gameObject.AddComponent<ObjectProgramRunner>();
                    added++;
                }

                if (t.childCount == 0) continue;
                var children = new Transform[t.childCount];
                for (var i = 0; i < children.Length; i++) children[i] = t.GetChild(i);
                added += AttachUnder(children, key);
            }
            return added;
        }

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
            var program = LoadProgram(); // the in-game editor's save, if it left one, else the scene's program
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

            // Last, once this runner is complete: a new ticker gives runners back to other saved objects as it wakes.
            if (Application.isPlaying) BlockyRuntimeTicker.EnsureExists();
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
