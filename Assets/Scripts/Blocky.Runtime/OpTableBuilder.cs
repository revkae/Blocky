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
    /// With no assemblies named, the scan covers Blocky.Runtime and every loaded assembly that references it —
    /// so a game's own blocks (in <c>Assembly-CSharp</c>, or an asmdef of its own) are found with no registration,
    /// and a game's op for a built-in key replaces Blocky's.
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

        /// <summary>
        /// All three tables for a running game, from every assembly that references Blocky.Runtime. A block whose op
        /// is missing or of the wrong kind is reported through <paramref name="reportMissing"/> and left empty — a
        /// script that runs it stops with a warning — instead of stopping every other block from working. (A block
        /// made in the editor whose script doesn't compile yet is exactly that case.)
        /// </summary>
        public static (IBlockOp[] steps, IConditionOp[] conditions, IValueOp[] values) BuildAllForPlay(BlockRegistry registry, Action<string> reportMissing)
        {
            var byKey = ScanExecutors(Array.Empty<Assembly>());
            return (BuildTable<IBlockOp>(registry, byKey, IsStep, reportMissing), BuildTable<IConditionOp>(registry, byKey, IsCondition, reportMissing),
                BuildTable<IValueOp>(registry, byKey, IsValue, reportMissing));
        }

        // Triggers aren't dispatched by the VM (TriggerBroker looks them up); conditions and reporters live in
        // their own tables, because they are only ever evaluated from inside another block's input.
        private static bool IsStep(BlockShape shape) =>
            shape != BlockShape.Trigger && shape != BlockShape.Boolean && shape != BlockShape.Reporter;

        private static bool IsCondition(BlockShape shape) => shape == BlockShape.Boolean;

        private static bool IsValue(BlockShape shape) => shape == BlockShape.Reporter;

        /// <param name="reportMissing">Null: a missing op throws. Otherwise it is told, and the entry stays empty.</param>
        private static T[] BuildTable<T>(BlockRegistry registry, Dictionary<string, object> byKey, Func<BlockShape, bool> include,
            Action<string> reportMissing = null) where T : class
        {
            var table = new T[registry.Count];
            for (var opcode = 0; opcode < registry.Count; opcode++)
            {
                var def = registry.GetByOpcode(opcode);
                if (!include(def.shape)) continue;

                if (!byKey.TryGetValue(def.executorKey ?? string.Empty, out var executor) || executor is not T op)
                {
                    var message = $"No {typeof(T).Name} bound to executorKey '{def.executorKey}' (blockType '{def.blockType}').";
                    if (reportMissing == null) throw new InvalidOperationException(message);
                    reportMissing(message);
                    continue;
                }
                table[opcode] = op;
            }
            return table;
        }

        private static Dictionary<string, object> ScanExecutors(Assembly[] assembliesToScan)
        {
            var assemblies = assembliesToScan.Length > 0 ? assembliesToScan : AssembliesWithBlocks();
            var runtime = typeof(OpTableBuilder).Assembly;
            var byKey = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var type in assemblies.SelectMany(TypesOf))
            {
                var attr = type.GetCustomAttribute<BlockExecutorAttribute>();
                if (attr == null) continue;

                if (!typeof(IBlockOp).IsAssignableFrom(type) && !typeof(IConditionOp).IsAssignableFrom(type) && !typeof(IValueOp).IsAssignableFrom(type))
                    throw new InvalidOperationException($"{type.FullName} has [BlockExecutor] but implements none of IBlockOp, IConditionOp, IValueOp.");

                // Blocky's own op may be replaced by a game's; any other clash is two ops claiming one block.
                if (byKey.TryGetValue(attr.ExecutorKey, out var existing) && (existing.GetType().Assembly != runtime || type.Assembly == runtime))
                    throw new InvalidOperationException($"Duplicate executorKey '{attr.ExecutorKey}': {existing.GetType().FullName} and {type.FullName}.");
                byKey[attr.ExecutorKey] = Activator.CreateInstance(type);
            }

            return byKey;
        }

        /// <summary>Blocky.Runtime first, then every loaded assembly that references it, by name — the same order every run.</summary>
        private static Assembly[] AssembliesWithBlocks()
        {
            var runtime = typeof(OpTableBuilder).Assembly;
            var runtimeName = runtime.GetName().Name;
            var others = new List<Assembly>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == runtime || assembly.IsDynamic) continue;
                if (Array.Exists(assembly.GetReferencedAssemblies(), reference => reference.Name == runtimeName)) others.Add(assembly);
            }

            others.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            others.Insert(0, runtime);
            return others.ToArray();
        }

        /// <summary>An assembly's types, minus any that can't be loaded (a missing optional dependency) rather than failing the whole scan.</summary>
        private static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }
    }
}
