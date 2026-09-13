using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blocky.Compiler;

namespace Blocky.Runtime
{
    /// <summary>
    /// Reflection-once binding of <c>executorKey</c> -> <see cref="IBlockOp"/> instance (TDD §7). Chosen over a
    /// source generator for now — cheapest path TDD §14 allows; revisit only if an IL2CPP/WebGL build shows
    /// stripping problems with reflection-bound ops.
    /// </summary>
    public static class OpTableBuilder
    {
        public static IBlockOp[] Build(BlockRegistry registry, params Assembly[] assembliesToScan)
        {
            var assemblies = assembliesToScan.Length > 0 ? assembliesToScan : new[] { typeof(OpTableBuilder).Assembly };
            var byKey = new Dictionary<string, IBlockOp>(StringComparer.Ordinal);

            foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
            {
                var attr = type.GetCustomAttribute<BlockExecutorAttribute>();
                if (attr == null) continue;

                if (!typeof(IBlockOp).IsAssignableFrom(type))
                    throw new InvalidOperationException($"{type.FullName} has [BlockExecutor] but does not implement IBlockOp.");
                if (!byKey.TryAdd(attr.ExecutorKey, (IBlockOp)Activator.CreateInstance(type)))
                    throw new InvalidOperationException($"Duplicate executorKey '{attr.ExecutorKey}'.");
            }

            var table = new IBlockOp[registry.Count];
            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var def = registry.GetByOpcode(opcode);
                if (def.shape == BlockShape.Trigger) continue; // triggers aren't dispatched by the VM, only looked up by TriggerBroker

                if (!byKey.TryGetValue(def.executorKey, out var op))
                    throw new InvalidOperationException($"No IBlockOp bound to executorKey '{def.executorKey}' (blockType '{def.blockType}').");
                table[opcode] = op;
            }

            return table;
        }
    }
}
