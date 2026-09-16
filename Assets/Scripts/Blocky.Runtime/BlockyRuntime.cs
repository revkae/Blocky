using Blocky.Compiler;
using Blocky.Runtime.Triggers;

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
        private static Playback _playback;

        public static BlockRegistry Registry => _registry ??= BlockRegistry.LoadFromResources();
        public static VmScheduler Scheduler => _scheduler ??= CreateScheduler();
        public static TriggerBroker Triggers => _triggers ??= new TriggerBroker();

        /// <summary>Every programmed object's start (for Reset) — runners record their object when they first start.</summary>
        public static WorldSnapshot World => _world ??= new WorldSnapshot();

        /// <summary>Go / Stop / Reset / Pause / Step forward and back / speed, for every script in the scene.</summary>
        public static Playback Playback => _playback ??= new Playback(Scheduler, Triggers, World);

        private static VmScheduler CreateScheduler()
        {
            var (steps, conditions) = OpTableBuilder.BuildBoth(Registry);
            return new VmScheduler(steps, conditions);
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
        }
    }
}
