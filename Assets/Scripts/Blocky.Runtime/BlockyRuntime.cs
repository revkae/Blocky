using Blocky.Compiler;
using Blocky.Runtime.Triggers;

namespace Blocky.Runtime
{
    /// <summary>
    /// The scheduler ticks once per frame from a single driver, and one shared <see cref="TriggerBroker"/>
    /// serves every <c>ObjectProgramRunner</c> in the scene (TDD §6.5, §6.7) — this is that shared state, built
    /// lazily on first use.
    /// </summary>
    public static class BlockyRuntime
    {
        private static BlockRegistry _registry;
        private static IBlockOp[] _opTable;
        private static IConditionOp[] _conditionTable;
        private static VmScheduler _scheduler;
        private static TriggerBroker _triggers;

        public static BlockRegistry Registry => _registry ??= BlockRegistry.LoadFromResources();
        private static IBlockOp[] OpTable => _opTable ??= OpTableBuilder.Build(Registry);
        private static IConditionOp[] ConditionTable => _conditionTable ??= OpTableBuilder.BuildConditions(Registry);
        public static VmScheduler Scheduler => _scheduler ??= new VmScheduler(OpTable, ConditionTable);
        public static TriggerBroker Triggers => _triggers ??= new TriggerBroker();

        /// <summary>Test-only hook: injects fixtures instead of the Resources-scanned registry/reflection-bound op table.</summary>
        public static void SetForTests(BlockRegistry registry, VmScheduler scheduler, TriggerBroker triggers)
        {
            _registry = registry;
            _scheduler = scheduler;
            _triggers = triggers;
        }

        /// <summary>Forces everything to rebuild from scratch on next access — e.g. between play sessions or after a test.</summary>
        public static void Reset()
        {
            _registry = null;
            _opTable = null;
            _conditionTable = null;
            _scheduler = null;
            _triggers = null;
        }
    }
}
