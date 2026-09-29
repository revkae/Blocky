using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Data
{
    /// <summary>
    /// Moves several selected blocks at once, by the same amount — or deletes them all (dropped on the palette).
    /// Each block is picked up the way a single drag would pick it up (<see cref="DropChain"/>): a hat or the first
    /// block of a loose stack takes the whole stack, a block takes everything below it, a condition leaves its slot
    /// empty. Every moved piece lands loose; a group never snaps, since there's no one edge it could connect by.
    /// A block another selected block already carries (a block under a selected hat, say) isn't moved on its own —
    /// see <see cref="Outermost"/>. One snapshot covers the whole move, so one Undo takes it all back.
    /// </summary>
    public sealed class MoveBlocks : IProgramCommand
    {
        private readonly List<BlockRef> _blocks;
        private readonly Dictionary<BlockRef, Vector2> _positions;
        private readonly Vector2 _delta;
        private readonly bool _discard;

        private BlockStack[] _before;

        private MoveBlocks(IReadOnlyList<BlockRef> blocks, IReadOnlyList<Vector2> positions, Vector2 delta, bool discard)
        {
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));
            if (positions != null && positions.Count != blocks.Count)
                throw new ArgumentException("One position per block.", nameof(positions));

            _blocks = new List<BlockRef>(blocks);
            _positions = new Dictionary<BlockRef, Vector2>();
            for (var i = 0; i < blocks.Count && positions != null; i++) _positions[blocks[i]] = positions[i];
            _delta = delta;
            _discard = discard;
        }

        /// <param name="positions">
        /// Where each block's top-left is on the table now (table units), used for a block that leaves its stack. A
        /// whole stack moves from its stored position instead.
        /// </param>
        /// <param name="delta">How far every block moves, in table units.</param>
        public static MoveBlocks By(IReadOnlyList<BlockRef> blocks, IReadOnlyList<Vector2> positions, Vector2 delta) =>
            new(blocks, positions ?? throw new ArgumentNullException(nameof(positions)), delta, discard: false);

        /// <summary>Deletes every block the way dropping it on the palette would: with everything it carries.</summary>
        public static MoveBlocks Discard(IReadOnlyList<BlockRef> blocks) => new(blocks, null, Vector2.zero, discard: true);

        public void Do(ProgramStore store)
        {
            var program = store.Program;
            _before = ProgramEdits.Snapshot(program);

            try
            {
                // Outermost blocks are disjoint subtrees, so picking one up never disturbs another; each is looked up
                // again right before it moves, because earlier moves shift indices in shared containers.
                foreach (var block in Outermost(program, _blocks))
                    MakeDrop(program, block)?.Execute(program);
            }
            catch
            {
                program.stacks = _before; // never leave a half-applied move behind
                throw;
            }

            store.RaiseChanged(new StructureChange(null, null, StructureChangeKind.ChainDropped));
        }

        public void Undo(ProgramStore store)
        {
            store.Program.stacks = _before;
            store.RaiseChanged(new StructureChange(null, null, StructureChangeKind.ChainDropped));
        }

        public string Describe() => _discard ? $"Delete {_blocks.Count} blocks" : $"Move {_blocks.Count} blocks";

        private DropChain MakeDrop(ObjectProgram program, BlockRef block)
        {
            if (block.IsHat)
            {
                var stack = ProgramQuery.FindStack(program, block.StackId);
                return stack == null ? null : DropChain.FromStack(stack.id, Target(stack.canvasPosition));
            }

            if (ProgramQuery.FindLocation(program, block.StackId, block.NodeId) is { } location)
            {
                var stack = ProgramQuery.FindStack(program, location.StackId);
                if (location.ParentNodeId == null && location.Index == 0 && ProgramQuery.IsLoose(stack))
                    return DropChain.FromStack(stack.id, Target(stack.canvasPosition)); // the whole loose stack: keep its id
                return DropChain.FromNodes(location, Target(PositionOf(block)));
            }

            if (ProgramQuery.TryFindConditionOwner(program, block.StackId, block.NodeId, out var owner, out var paramKey))
                return DropChain.FromConditionSlot(block.StackId, owner.id, paramKey, Target(PositionOf(block)));

            return null; // gone already
        }

        private ChainTarget Target(Vector2 from) => _discard ? ChainTarget.Discard() : ChainTarget.Free(from + _delta);

        private Vector2 PositionOf(BlockRef block) => _positions.TryGetValue(block, out var position) ? position : Vector2.zero;

        /// <summary>
        /// The blocks that move on their own: every block in <paramref name="blocks"/> that exists, isn't a repeat, and
        /// isn't already carried along by another one — a block under a selected hat, a block further down the same
        /// sequence, a block inside a selected C-block's mouth, a condition in a selected block's slot. In the given order.
        /// </summary>
        public static List<BlockRef> Outermost(ObjectProgram program, IReadOnlyList<BlockRef> blocks)
        {
            var unique = new List<BlockRef>();
            var carried = new List<HashSet<string>>();
            foreach (var block in blocks)
            {
                if (unique.Contains(block)) continue;
                var ids = Carried(program, block);
                if (ids == null) continue; // not on the table
                unique.Add(block);
                carried.Add(ids);
            }

            var outermost = new List<BlockRef>();
            for (var i = 0; i < unique.Count; i++)
            {
                var isCarried = false;
                for (var j = 0; j < unique.Count && !isCarried; j++)
                    isCarried = j != i && !unique[i].IsHat && carried[j].Contains(unique[i].NodeId);
                if (!isCarried) outermost.Add(unique[i]);
            }
            return outermost;
        }

        /// <summary>Ids of every node that moves when <paramref name="block"/> is picked up; null if the block isn't there.</summary>
        private static HashSet<string> Carried(ObjectProgram program, BlockRef block)
        {
            var ids = new HashSet<string>();

            if (block.IsHat)
            {
                var stack = ProgramQuery.FindStack(program, block.StackId);
                if (stack == null || ProgramQuery.IsLoose(stack)) return null;
                foreach (var node in stack.sequence) AddSubtree(node, ids);
                return ids;
            }

            if (ProgramQuery.FindLocation(program, block.StackId, block.NodeId) is { } location)
            {
                var sequence = ProgramQuery.Resolve(program, location).Get();
                for (var i = location.Index; i < sequence.Length; i++) AddSubtree(sequence[i], ids); // Scratch's rule: the tail comes too
                return ids;
            }

            var condition = ProgramQuery.FindNode(program, block.StackId, block.NodeId);
            if (condition == null) return null;
            AddSubtree(condition, ids);
            return ids;
        }

        private static void AddSubtree(BlockNode node, HashSet<string> ids)
        {
            if (node == null) return;
            ids.Add(node.id);
            foreach (var param in node.parameters)
                if (param?.reporter != null) AddSubtree(param.reporter, ids);
            foreach (var branch in node.branches)
                foreach (var child in branch) AddSubtree(child, ids);
        }
    }

    /// <summary>
    /// Deletes every selected block, each as <see cref="DeleteBlock"/> would delete it alone (a C-block takes its
    /// mouth, a deleted hat leaves its blocks loose). A block that went with an earlier one is skipped. One Undo.
    /// </summary>
    public sealed class DeleteBlocks : IProgramCommand
    {
        private readonly List<BlockRef> _blocks;
        private BlockStack[] _before;

        public DeleteBlocks(IReadOnlyList<BlockRef> blocks)
        {
            _blocks = new List<BlockRef>(blocks ?? throw new ArgumentNullException(nameof(blocks)));
        }

        public void Do(ProgramStore store)
        {
            var program = store.Program;
            _before = ProgramEdits.Snapshot(program);
            foreach (var block in _blocks) DeleteBlock.TryDelete(program, block.StackId, block.NodeId);
            store.RaiseChanged(new StructureChange(null, null, StructureChangeKind.NodeRemoved));
        }

        public void Undo(ProgramStore store)
        {
            store.Program.stacks = _before;
            store.RaiseChanged(new StructureChange(null, null, StructureChangeKind.NodeInserted));
        }

        public string Describe() => $"Delete {_blocks.Count} blocks";
    }
}
