using System;

namespace Blocky.Data
{
    /// <summary>
    /// The single command behind every drag on the table (TDD §8.3: exactly one command per drag). A chain is
    /// picked up from one of four sources — a new block or a new hat from the palette, a node plus everything
    /// below it in its container, or a whole stack — and placed at a <see cref="ChainTarget"/>.
    /// Picking up a node takes the whole tail under it, because that's what grabbing a block does in Scratch.
    /// A loose stack left empty by the pick-up is removed; a triggered stack is kept even when empty, since a
    /// lone hat on the table is still something the user placed.
    /// Undo restores a snapshot taken in <see cref="Do"/>: one drop can touch two containers and add or remove
    /// stacks, and replaying that backwards step by step would be far more fragile than restoring a copy.
    /// </summary>
    public sealed class DropChain : IProgramCommand
    {
        private enum SourceKind { NewNode, NewTrigger, Nodes, Stack, ConditionSlot }

        private readonly SourceKind _source;
        private readonly ChainTarget _target;
        private readonly BlockNode _newNode;
        private readonly string _newTriggerType;
        private readonly BlockParam[] _newTriggerParameters;
        private readonly NodeLocation _from;
        private readonly string _fromStackId;
        private readonly string _fromOwnerNodeId;
        private readonly string _fromParamKey;

        private BlockStack[] _before;

        private DropChain(SourceKind source, ChainTarget target, BlockNode newNode = null, string newTriggerType = null,
            BlockParam[] newTriggerParameters = null, NodeLocation from = default, string fromStackId = null,
            string fromOwnerNodeId = null, string fromParamKey = null)
        {
            _source = source;
            _target = target;
            _newNode = newNode;
            _newTriggerType = newTriggerType;
            _newTriggerParameters = newTriggerParameters ?? Array.Empty<BlockParam>();
            _from = from;
            _fromStackId = fromStackId;
            _fromOwnerNodeId = fromOwnerNodeId;
            _fromParamKey = fromParamKey;
        }

        public static DropChain FromNewNode(BlockNode node, ChainTarget target) =>
            new(SourceKind.NewNode, target, newNode: node ?? throw new ArgumentNullException(nameof(node)));

        public static DropChain FromNewTrigger(string triggerBlockType, BlockParam[] triggerParameters, ChainTarget target) =>
            new(SourceKind.NewTrigger, target, newTriggerType: triggerBlockType, newTriggerParameters: triggerParameters);

        public static DropChain FromNodes(NodeLocation from, ChainTarget target) => new(SourceKind.Nodes, target, from: from);

        public static DropChain FromStack(string stackId, ChainTarget target) => new(SourceKind.Stack, target, fromStackId: stackId);

        /// <summary>Takes the condition block out of <paramref name="ownerNodeId"/>'s slot <paramref name="paramKey"/>, leaving the slot empty.</summary>
        public static DropChain FromConditionSlot(string stackId, string ownerNodeId, string paramKey, ChainTarget target) =>
            new(SourceKind.ConditionSlot, target, fromStackId: stackId, fromOwnerNodeId: ownerNodeId, fromParamKey: paramKey);

        public void Do(ProgramStore store)
        {
            var program = store.Program;
            _before = ProgramEdits.Snapshot(program);

            try
            {
                Execute(program);
            }
            catch
            {
                program.stacks = _before; // never leave a half-applied drop behind
                throw;
            }

            store.RaiseChanged(new StructureChange(_target.StackId ?? _target.InsertAt.StackId, null, StructureChangeKind.ChainDropped));
        }

        public void Undo(ProgramStore store)
        {
            store.Program.stacks = _before;
            store.RaiseChanged(new StructureChange(null, null, StructureChangeKind.ChainDropped));
        }

        public string Describe() => $"Drop chain ({_source} -> {_target.Kind})";

        /// <summary>The pick-up and placement alone — no snapshot, no rollback, no change event. <see cref="MoveBlocks"/> runs several under one snapshot.</summary>
        internal void Execute(ObjectProgram program)
        {
            PickUp(program, out var chain, out var trigger, out var triggerParameters, out var reusedStackId);
            Place(program, chain, trigger, triggerParameters, reusedStackId);
        }

        private void PickUp(ObjectProgram program, out BlockNode[] chain, out string trigger, out BlockParam[] triggerParameters, out string reusedStackId)
        {
            trigger = null;
            triggerParameters = Array.Empty<BlockParam>();
            reusedStackId = null;

            switch (_source)
            {
                case SourceKind.NewNode:
                    chain = new[] { _newNode };
                    return;

                case SourceKind.NewTrigger:
                    chain = Array.Empty<BlockNode>();
                    trigger = _newTriggerType;
                    triggerParameters = _newTriggerParameters;
                    return;

                case SourceKind.Nodes:
                {
                    var container = ProgramQuery.Resolve(program, _from);
                    var all = container.Get();
                    chain = ArrayUtil.Slice(all, _from.Index, all.Length - _from.Index);
                    container.Set(ArrayUtil.Slice(all, 0, _from.Index));

                    var source = ProgramQuery.FindStack(program, _from.StackId);
                    if (ProgramQuery.IsLoose(source) && source.sequence.Length == 0) ProgramEdits.RemoveStack(program, source.id);
                    return;
                }

                case SourceKind.ConditionSlot:
                {
                    var owner = ProgramQuery.FindNode(program, _fromStackId, _fromOwnerNodeId)
                        ?? throw new InvalidOperationException($"Node '{_fromOwnerNodeId}' not found in stack '{_fromStackId}'.");
                    var condition = ProgramQuery.SetCondition(owner, _fromParamKey, null)
                        ?? throw new InvalidOperationException($"Slot '{_fromParamKey}' of '{_fromOwnerNodeId}' is empty.");
                    chain = new[] { condition };
                    return;
                }

                default: // Stack
                {
                    var stack = ProgramQuery.FindStack(program, _fromStackId)
                        ?? throw new InvalidOperationException($"Stack '{_fromStackId}' not found.");
                    ProgramEdits.RemoveStack(program, stack.id);
                    chain = stack.sequence;
                    trigger = stack.triggerBlockType;
                    triggerParameters = stack.triggerParameters;
                    reusedStackId = stack.id;
                    return;
                }
            }
        }

        private void Place(ObjectProgram program, BlockNode[] chain, string trigger, BlockParam[] triggerParameters, string reusedStackId)
        {
            var hasTrigger = !string.IsNullOrEmpty(trigger);

            switch (_target.Kind)
            {
                case ChainTargetKind.Discard:
                    return;

                case ChainTargetKind.Free:
                    ProgramEdits.AppendStack(program, new BlockStack
                    {
                        id = reusedStackId ?? IdGenerator.NewId(),
                        triggerBlockType = hasTrigger ? trigger : string.Empty,
                        triggerParameters = triggerParameters,
                        sequence = chain,
                        canvasPosition = _target.Position
                    });
                    return;

                case ChainTargetKind.Insert:
                {
                    if (hasTrigger)
                        throw new InvalidOperationException("A hat block can only start a stack; it can't be inserted into one.");
                    var container = ProgramQuery.Resolve(program, _target.InsertAt);
                    container.Set(ArrayUtil.InsertRange(container.Get(), _target.InsertAt.Index, chain));
                    return;
                }

                case ChainTargetKind.ConditionSlot:
                {
                    if (hasTrigger || chain.Length != 1)
                        throw new InvalidOperationException("Only a single condition block fits in a condition slot.");
                    var owner = ProgramQuery.FindNode(program, _target.StackId, _target.NodeId)
                        ?? throw new InvalidOperationException($"Node '{_target.NodeId}' not found in stack '{_target.StackId}'.");

                    // Scratch's rule: dropping onto a filled slot swaps — the condition that was there pops out onto the table.
                    var displaced = ProgramQuery.SetCondition(owner, _target.ParamKey, chain[0]);
                    if (displaced != null)
                        ProgramEdits.AppendStack(program, new BlockStack
                        {
                            id = IdGenerator.NewId(),
                            triggerBlockType = string.Empty,
                            sequence = new[] { displaced },
                            canvasPosition = _target.Position
                        });
                    return;
                }

                default: // AttachAbove
                {
                    var stack = ProgramQuery.FindStack(program, _target.StackId)
                        ?? throw new InvalidOperationException($"Stack '{_target.StackId}' not found.");
                    if (!ProgramQuery.IsLoose(stack))
                        throw new InvalidOperationException("Can only attach above a stack that has no hat block.");

                    stack.sequence = ArrayUtil.InsertRange(stack.sequence, 0, chain);
                    stack.canvasPosition = _target.Position;
                    if (hasTrigger)
                    {
                        stack.triggerBlockType = trigger;
                        stack.triggerParameters = triggerParameters;
                    }
                    return;
                }
            }
        }
    }
}
