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
    /// reflection-bound ops. Two tables, both indexed by opcode: <see cref="Build"/> for blocks the VM steps
    /// through (<see cref="IBlockOp"/>) and <see cref="BuildConditions"/> for condition blocks
    /// (<see cref="IConditionOp"/>), which are only ever evaluated from inside another block's slot.
    /// </summary>
    public static class OpTableBuilder
    {
        public static IBlockOp[] Build(BlockRegistry registry, params Assembly[] assembliesToScan)
        {
            var byKey = ScanExecutors(assembliesToScan);
            var table = new IBlockOp[registry.Count];

            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var def = registry.GetByOpcode(opcode);
                if (def.shape == BlockShape.Trigger) continue; // triggers aren't dispatched by the VM, only looked up by TriggerBroker
                if (def.shape == BlockShape.Boolean) continue; // conditions live in BuildConditions' table

                if (!byKey.TryGetValue(def.executorKey ?? string.Empty, out var executor) || executor is not IBlockOp op)
                    throw new InvalidOperationException($"No IBlockOp bound to executorKey '{def.executorKey}' (blockType '{def.blockType}').");
                table[opcode] = op;
            }

            return table;
        }

        public static IConditionOp[] BuildConditions(BlockRegistry registry, params Assembly[] assembliesToScan)
        {
            var byKey = ScanExecutors(assembliesToScan);
            var table = new IConditionOp[registry.Count];

            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var def = registry.GetByOpcode(opcode);
                if (def.shape != BlockShape.Boolean) continue;

                if (!byKey.TryGetValue(def.executorKey ?? string.Empty, out var executor) || executor is not IConditionOp op)
                    throw new InvalidOperationException($"No IConditionOp bound to executorKey '{def.executorKey}' (blockType '{def.blockType}').");
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
