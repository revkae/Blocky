using System;

namespace Blocky.Data
{
    /// <summary>Read access into an <see cref="ObjectProgram"/> and the mutable-array resolution commands need.</summary>
    public static class ProgramQuery
    {
        public static BlockStack FindStack(ObjectProgram program, string stackId)
        {
            foreach (var s in program.stacks)
                if (s.id == stackId) return s;
            return null;
        }

        /// <summary>A stack with no hat block — blocks left lying on the table. Saved like any other stack, but never compiled or run (Scratch's loose blocks).</summary>
        public static bool IsLoose(BlockStack stack) => string.IsNullOrEmpty(stack.triggerBlockType);

        public static BlockNode FindNode(ObjectProgram program, string stackId, string nodeId)
        {
            var stack = FindStack(program, stackId);
            return stack == null ? null : FindInSequence(stack.sequence, nodeId);
        }

        private static BlockNode FindInSequence(BlockNode[] sequence, string nodeId)
        {
            foreach (var node in sequence)
            {
                if (node.id == nodeId) return node;
                foreach (var branch in node.branches)
                {
                    var found = FindInSequence(branch, nodeId);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>Finds where a node currently sits (its container + index), for building a <c>RemoveNode</c>/<c>MoveNode</c>/insert-after location without the caller having to track it separately.</summary>
        public static NodeLocation? FindLocation(ObjectProgram program, string stackId, string nodeId)
        {
            var stack = FindStack(program, stackId);
            return stack == null ? null : FindLocationInSequence(stack.sequence, stackId, null, -1, nodeId);
        }

        private static NodeLocation? FindLocationInSequence(BlockNode[] sequence, string stackId, string parentNodeId, int branchIndex, string nodeId)
        {
            for (var i = 0; i < sequence.Length; i++)
            {
                if (sequence[i].id == nodeId) return new NodeLocation(stackId, parentNodeId, branchIndex, i);

                for (var b = 0; b < sequence[i].branches.Length; b++)
                {
                    var found = FindLocationInSequence(sequence[i].branches[b], stackId, sequence[i].id, b, nodeId);
                    if (found != null) return found;
                }
            }
            return null;
        }

        internal static NodeContainer Resolve(ObjectProgram program, NodeLocation location)
        {
            var stack = FindStack(program, location.StackId)
                ?? throw new InvalidOperationException($"Stack '{location.StackId}' not found.");

            if (location.ParentNodeId == null)
                return new NodeContainer(stack);

            var parent = FindNode(program, location.StackId, location.ParentNodeId)
                ?? throw new InvalidOperationException($"Node '{location.ParentNodeId}' not found in stack '{location.StackId}'.");

            return new NodeContainer(parent, location.BranchIndex);
        }
    }

    /// <summary>A get/set view onto whichever array a <see cref="NodeLocation"/> points at.</summary>
    internal readonly struct NodeContainer
    {
        private readonly BlockStack _stack;
        private readonly BlockNode _parentNode;
        private readonly int _branchIndex;

        public NodeContainer(BlockStack stack)
        {
            _stack = stack;
            _parentNode = null;
            _branchIndex = -1;
        }

        public NodeContainer(BlockNode parentNode, int branchIndex)
        {
            _stack = null;
            _parentNode = parentNode;
            _branchIndex = branchIndex;
        }

        public BlockNode[] Get() => _parentNode != null ? _parentNode.branches[_branchIndex] : _stack.sequence;

        public void Set(BlockNode[] value)
        {
            if (_parentNode != null) _parentNode.branches[_branchIndex] = value;
            else _stack.sequence = value;
        }
    }
}
