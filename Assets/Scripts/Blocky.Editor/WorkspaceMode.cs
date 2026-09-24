using System.Collections.Generic;
using Blocky.Data;
using UnityEngine;

// The statics here are fixed values that never change, so there is nothing to reset between Play sessions.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Editor
{
    /// <summary>How strict the table is about where blocks are allowed to go.</summary>
    public enum WorkspaceMode
    {
        /// <summary>
        /// Scratch's table: blocks go anywhere, several scripts sit side by side, blocks may lie loose with no
        /// event on top, and the surface pans in both directions and zooms.
        /// </summary>
        Free,

        /// <summary>
        /// One column, the way Delightex's CoBlocks does it: every script is laid out top to bottom in a single
        /// numbered column whatever position it was saved at, a chain that doesn't snap joins the script it was
        /// dropped nearest instead of lying loose, and the surface only scrolls up and down. There is one place a
        /// block can go — that is the whole point of the mode, for a learner who hasn't built a mental map of a
        /// canvas yet. The program itself is unchanged: the same file opens either way.
        /// </summary>
        Simple
    }

    /// <summary>
    /// Where a dropped chain lands in <see cref="WorkspaceMode.Simple"/> when it snapped to nothing — the rule
    /// that replaces Free mode's "it lies loose where you dropped it". Pure, so it is tested without a panel,
    /// a pointer or a layout pass.
    /// </summary>
    public static class SimpleDropPolicy
    {
        // Matches ProgramCanvasView's nominal stack size: only used to find a spot that doesn't overlap, for the
        // benefit of Free mode later — Simple mode lays stacks out in a column and ignores stored positions.
        private static readonly Vector2 NominalStackSize = new(240f, 160f);

        /// <param name="nearestStackId">The script the drop landed nearest, or null when that isn't known.</param>
        /// <returns>Where the chain goes, or null to refuse the drop — the blocks then go back where they came from.</returns>
        public static ChainTarget? Fallback(ObjectProgram program, ChainShape shape, string nearestStackId)
        {
            // A condition only ever lives in a hexagonal hole. Anywhere else it would lie loose on the table,
            // which this mode doesn't have, so the drop is refused instead.
            if (shape.IsCondition) return null;

            // A hat can't be inserted into a script (DropChain enforces that), so it starts another one at the
            // bottom of the column. It still gets a position clear of the others, so the same program opened in
            // Free mode doesn't have its scripts stacked on top of each other.
            if (shape.HasHat) return ChainTarget.Free(NextFreePosition(program));

            var stack = ProgramQuery.FindStack(program, nearestStackId) ?? LastStack(program);
            return stack == null
                ? ChainTarget.Free(NextFreePosition(program)) // nothing on the table yet: this chain starts the column
                : ChainTarget.Insert(NodeLocation.InStack(stack.id, stack.sequence.Length));
        }

        private static BlockStack LastStack(ObjectProgram program) =>
            program.stacks.Length == 0 ? null : program.stacks[^1];

        private static Vector2 NextFreePosition(ObjectProgram program)
        {
            var taken = new List<Vector2>(program.stacks.Length);
            foreach (var stack in program.stacks) taken.Add(stack.canvasPosition);
            return StackPlacementResolver.FindFreePosition(Vector2.zero, taken, NominalStackSize);
        }
    }
}
