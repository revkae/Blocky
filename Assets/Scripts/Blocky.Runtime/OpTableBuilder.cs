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
    /// condition blocks (<see cref="IConditionOp"/>), which are only ever evaluated from inside another block's
    /// slot. <see cref="BuildBoth"/> fills both from a single scan.
    /// </summary>
    public static class OpTableBuilder
    {
        public static IBlockOp[] Build(BlockRegistry registry, params Assembly[] assembliesToScan) =>
            BuildTable<IBlockOp>(registry, ScanExecutors(assembliesToScan), IsStep);

        public static IConditionOp[] BuildConditions(BlockRegistry registry, params Assembly[] assembliesToScan) =>
            BuildTable<IConditionOp>(registry, ScanExecutors(assembliesToScan), IsCondition);

        /// <summary>Both tables from one reflection scan — what <see cref="BlockyRuntime"/> uses at boot.</summary>
        public static (IBlockOp[] steps, IConditionOp[] conditions) BuildBoth(BlockRegistry registry, params Assembly[] assembliesToScan)
        {
            var byKey = ScanExecutors(assembliesToScan);
            return (BuildTable<IBlockOp>(registry, byKey, IsStep), BuildTable<IConditionOp>(registry, byKey, IsCondition));
        }

        // Triggers aren't dispatched by the VM (TriggerBroker looks them up); conditions live in their own table.
        private static bool IsStep(BlockShape shape) => shape != BlockShape.Trigger && shape != BlockShape.Boolean;

        private static bool IsCondition(BlockShape shape) => shape == BlockShape.Boolean;

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

                if (!typeof(IBlockOp).IsAssignableFrom(type) && !typeof(IConditionOp).IsAssignableFrom(type))
                    throw new InvalidOperationException($"{type.FullName} has [BlockExecutor] but implements neither IBlockOp nor IConditionOp.");
                if (!byKey.TryAdd(attr.ExecutorKey, Activator.CreateInstance(type)))
                    throw new InvalidOperationException($"Duplicate executorKey '{attr.ExecutorKey}'.");
            }

            return byKey;
        }
    }
}
