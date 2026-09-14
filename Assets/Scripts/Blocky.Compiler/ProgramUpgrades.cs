using System;
using Blocky.Data;

namespace Blocky.Compiler
{
    /// <summary>
    /// Brings programs saved by older builds up to the current block catalog, in place. Needs the registry (to
    /// know which params are condition slots now), which is why it lives here and not in the schema migrations.
    /// Every load path calls it — the in-game editor, the Editor window and <c>ObjectProgramRunner</c> — so the
    /// compiler only ever sees the current format.
    /// </summary>
    public static class ProgramUpgrades
    {
        public const string TrueConditionType = "condition.true";

        /// <summary>
        /// Condition params used to be checkboxes. A ticked one becomes a <c>true</c> condition block in the slot,
        /// an unticked one an empty slot (which reads as false) — so every old script behaves exactly as before,
        /// and now looks the way it runs. Returns true if anything changed.
        /// </summary>
        public static bool UpgradeCheckboxConditions(ObjectProgram program, BlockRegistry registry)
        {
            var changed = false;
            foreach (var stack in program.stacks)
                changed |= UpgradeSequence(stack.sequence, registry);
            return changed;
        }

        private static bool UpgradeSequence(BlockNode[] sequence, BlockRegistry registry)
        {
            var changed = false;
            var trueDefinition = registry.Find(TrueConditionType);
            foreach (var node in sequence)
            {
                var def = registry.Find(node.blockType);
                if (def != null)
                    foreach (var spec in def.parameters)
                    {
                        if (spec.kind != ParamKind.Reporter) continue;
                        var index = Array.FindIndex(node.parameters, p => p.key == spec.key);
                        if (index < 0 || node.parameters[index].kind != ParamKind.Bool) continue;

                        var wasTicked = node.parameters[index].boolean;
                        node.parameters[index] = new BlockParam
                        {
                            key = spec.key,
                            kind = ParamKind.Reporter,
                            reporter = wasTicked && trueDefinition != null ? BlockNodes.Instantiate(trueDefinition) : null
                        };
                        changed = true;
                    }

                foreach (var branch in node.branches)
                    changed |= UpgradeSequence(branch, registry);
            }
            return changed;
        }
    }
}
