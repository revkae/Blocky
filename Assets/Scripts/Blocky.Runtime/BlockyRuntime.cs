using Blocky.Compiler;
using Blocky.Runtime.Triggers;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime
{
    /// <summary>
    /// The scheduler ticks once per frame from a single driver, and one shared <see cref="TriggerBroker"/>
    /// serves every <c>ObjectProgramRunner</c> in the scene (TDD §6.5, §6.7) — this is that shared state, built
    /// lazily on first use, plus the scene-wide <see cref="Playback"/> commands behind the editor's run bar.
    /// </summary>
    public static class BlockyRuntime
    {
        private static BlockRegistry _registry;
        private static VmScheduler _scheduler;
        private static TriggerBroker _triggers;
        private static WorldSnapshot _world;
        private static BlockyVariables _variables;
        private static BlockyObjects _objects;
        private static BlockyClones _clones;
        private static BlockyAudio _audio;
        private static Playback _playback;

        public static BlockRegistry Registry => _registry ??= BlockRegistry.LoadFromResources();
        public static VmScheduler Scheduler => _scheduler ??= CreateScheduler();
        public static TriggerBroker Triggers => _triggers ??= new TriggerBroker();

        /// <summary>Every programmed object's start (for Reset) — runners record their object when they first start.</summary>
        public static WorldSnapshot World => _world ??= new WorldSnapshot();

        /// <summary>Named values every script can set and read back, shared or per-object (Scratch's variables).</summary>
        public static BlockyVariables Variables => _variables ??= new BlockyVariables();

        /// <summary>Name -> GameObject, for the blocks where one object talks about another.</summary>
        public static BlockyObjects Objects => _objects ??= new BlockyObjects();

        /// <summary>Copies of objects made while the game runs, and the cap that keeps a runaway loop from taking the machine with it.</summary>
        public static BlockyClones Clones => _clones ??= new BlockyClones();

        /// <summary>Notes and clips, generated rather than imported — see <see cref="BlockyAudio"/>.</summary>
        public static BlockyAudio Audio => _audio ??= new BlockyAudio();

        /// <summary>Go / Stop / Reset / Pause / Step forward and back / speed, for every script in the scene.</summary>
        public static Playback Playback => _playback ??= new Playback(Scheduler, Triggers, World, Clones, Audio);

        private static VmScheduler CreateScheduler()
        {
            var (steps, conditions, values) = OpTableBuilder.BuildAll(Registry);
            return new VmScheduler(steps, conditions, values);
        }

        /// <summary>Test-only hook: injects fixtures instead of the Resources-scanned registry/reflection-bound op table.</summary>
        public static void SetForTests(BlockRegistry registry, VmScheduler scheduler, TriggerBroker triggers)
        {
            _registry = registry;
            _scheduler = scheduler;
            _triggers = triggers;
            _playback = null; // rebuilt around the injected scheduler and broker
        }

        /// <summary>
        /// Every Play session starts from nothing. The project enters Play mode without a domain reload, so statics
        /// survive from the last session — without this, a Pause or Step there left the scheduler paused, "when Play
        /// clicked" already spent and the step history full of destroyed objects, and Go and Step did nothing.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession() => Reset();

        /// <summary>Forces everything to rebuild from scratch on next access — e.g. between play sessions or after a test.</summary>
        public static void Reset()
        {
            _registry = null;
            _scheduler = null;
            _triggers = null;
            _world = null;
            _playback = null;
            _variables = null;
            _objects = null;
            _clones = null;
            _audio = null;
        }
    }
}
