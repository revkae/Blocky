using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blocky.Compiler;

namespace Blocky.Runtime
{
    /// <summary>
    /// Reflection-once binding of <c>executorKey</c> -> op instance (TDD §7). Chosen over a source generator for
    /// now — cheapest path TDD §14 allows; revisit only if an IL2CPP/WebGL build shows stripping problems with
    /// reflection-bound ops. Two tables, both indexed by opcode: steps the VM runs (<see cref="IBlockOp"/>) and
    /// condition blocks (<see cref="IConditionOp"/>) and reporters (<see cref="IValueOp"/>), which are only ever
    /// evaluated from inside another block's input. <see cref="BuildAll"/> fills all three from a single scan.
    /// </summary>
    public static class OpTableBuilder
    {
        public static IBlockOp[] Build(BlockRegistry registry, params Assembly[] assembliesToScan) =>
            BuildTable<IBlockOp>(registry, ScanExecutors(assembliesToScan), IsStep);

        public static IConditionOp[] BuildConditions(BlockRegistry registry, params Assembly[] assembliesToScan) =>
            BuildTable<IConditionOp>(registry, ScanExecutors(assembliesToScan), IsCondition);

        public static IValueOp[] BuildValues(BlockRegistry registry, params Assembly[] assembliesToScan) =>
            BuildTable<IValueOp>(registry, ScanExecutors(assembliesToScan), IsValue);

        /// <summary>Steps and conditions from one reflection scan.</summary>
        public static (IBlockOp[] steps, IConditionOp[] conditions) BuildBoth(BlockRegistry registry, params Assembly[] assembliesToScan)
        {
            var byKey = ScanExecutors(assembliesToScan);
            return (BuildTable<IBlockOp>(registry, byKey, IsStep), BuildTable<IConditionOp>(registry, byKey, IsCondition));
        }

        /// <summary>All three tables from one reflection scan — what <see cref="BlockyRuntime"/> uses at boot.</summary>
        public static (IBlockOp[] steps, IConditionOp[] conditions, IValueOp[] values) BuildAll(BlockRegistry registry, params Assembly[] assembliesToScan)
        {
            var byKey = ScanExecutors(assembliesToScan);
            return (BuildTable<IBlockOp>(registry, byKey, IsStep), BuildTable<IConditionOp>(registry, byKey, IsCondition),
                BuildTable<IValueOp>(registry, byKey, IsValue));
        }

        // Triggers aren't dispatched by the VM (TriggerBroker looks them up); conditions and reporters live in
        // their own tables, because they are only ever evaluated from inside another block's input.
        private static bool IsStep(BlockShape shape) =>
            shape != BlockShape.Trigger && shape != BlockShape.Boolean && shape != BlockShape.Reporter;

        private static bool IsCondition(BlockShape shape) => shape == BlockShape.Boolean;

        private static bool IsValue(BlockShape shape) => shape == BlockShape.Reporter;

        private static T[] BuildTable<T>(BlockRegistry registry, Dictionary<string, object> byKey, Func<BlockShape, bool> include) where T : class
        {
            var table = new T[registry.Count];
            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var def = registry.GetByOpcode(opcode);
                if (!include(def.shape)) continue;

                if (!byKey.TryGetValue(def.executorKey ?? string.Empty, out var executor) || executor is not T op)
                    throw new InvalidOperationException($"No {typeof(T).Name} bound to executorKey '{def.executorKey}' (blockType '{def.blockType}').");
                table[opcode] = op;
            }
            return table;
        }

        private static Dictionary<string, object> ScanExecutors(Assembly[] assembliesToScan)
        {
            var assemblies = assembliesToScan.Length > 0 ? assembliesToScan : new[] { typeof(OpTableBuilder).Assembly };
            var byKey = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
            {
                var attr = type.GetCustomAttribute<BlockExecutorAttribute>();
                if (attr == null) continue;

                if (!typeof(IBlockOp).IsAssignableFrom(type) && !typeof(IConditionOp).IsAssignableFrom(type) && !typeof(IValueOp).IsAssignableFrom(type))
                    throw new InvalidOperationException($"{type.FullName} has [BlockExecutor] but implements none of IBlockOp, IConditionOp, IValueOp.");
                if (!byKey.TryAdd(attr.ExecutorKey, Activator.CreateInstance(type)))
                    throw new InvalidOperationException($"Duplicate executorKey '{attr.ExecutorKey}'.");
            }

            return byKey;
        }
    }
}
