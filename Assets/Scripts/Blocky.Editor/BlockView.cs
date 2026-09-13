using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// One VisualElement subtree per node (TDD §8.2). Holds the node's <see cref="NodeId"/>, never a reference
    /// to the <see cref="BlockNode"/> itself, so a rebuilt data model never leaves a stale pointer.
    /// Read-only (Milestone 4) unless a <see cref="ProgramStore"/> is supplied, in which case param fields
    /// become live and a delete button plus a per-branch "add" affordance appear (the authoring window).
    /// </summary>
    public sealed class BlockView : VisualElement
    {
        public string NodeId { get; }
        public string StackId { get; }

        private readonly List<VisualElement> _bodySlots = new();

        /// <summary>One entry per branch (TDD §8.2's <c>bodySlot</c>), in branch order — used by <see cref="DropCandidateBuilder"/>.</summary>
        public IReadOnlyList<VisualElement> BodySlots => _bodySlots;

        /// <summary>Builds a normal block view, or an <see cref="UnknownBlockView"/> if the block type no longer resolves.</summary>
        public static VisualElement Create(BlockNode node, BlockRegistry registry, string stackId = null, ProgramStore store = null)
        {
            var definition = registry.Find(node.blockType);
            return definition != null ? new BlockView(node, definition, registry, stackId, store) : new UnknownBlockView(node);
        }

        private BlockView(BlockNode node, BlockDefinition definition, BlockRegistry registry, string stackId, ProgramStore store)
        {
            NodeId = node.id;
            StackId = stackId;

            AddToClassList("blocky-block");
            AddToClassList($"blocky-block--category-{definition.category.ToString().ToLowerInvariant()}");
            AddToClassList($"blocky-block--shape-{definition.shape.ToString().ToLowerInvariant()}");

            var header = new VisualElement();
            header.AddToClassList("blocky-block__header");
            header.Add(new Label(string.IsNullOrEmpty(definition.displayNameKey) ? definition.blockType : definition.displayNameKey));

            foreach (var spec in definition.parameters)
            {
                var value = Array.Find(node.parameters, p => p.key == spec.key);
                if (store != null)
                {
                    header.Add(ParamFieldFactory.CreateLiveField(spec, value, newValue =>
                        store.Apply(new SetParam(new ParamTarget(stackId, node.id), spec.key, newValue))));
                }
                else
                {
                    header.Add(ParamFieldFactory.CreateReadOnlyField(spec, value));
                }
            }

            if (store != null)
            {
                var deleteButton = new Button(() =>
                {
                    var location = ProgramQuery.FindLocation(store.Program, stackId, node.id);
                    if (location != null) store.Apply(new RemoveNode(location.Value));
                }) { text = "✕" };
                deleteButton.AddToClassList("blocky-block__delete");
                header.Add(deleteButton);
            }

            Add(header);

            for (var branchIndex = 0; branchIndex < definition.branchCount; branchIndex++)
            {
                var slot = new VisualElement();
                slot.AddToClassList("blocky-block__body-slot");

                if (branchIndex < node.branches.Length)
                    foreach (var child in node.branches[branchIndex])
                        slot.Add(Create(child, registry, stackId, store));

                if (store != null)
                    slot.Add(BuildAddButton(registry, store, stackId, node.id, branchIndex));

                _bodySlots.Add(slot);
                Add(slot);
            }
        }

        private static VisualElement BuildAddButton(BlockRegistry registry, ProgramStore store, string stackId, string parentNodeId, int branchIndex)
        {
            var container = new VisualElement();
            container.AddToClassList("blocky-block__add-container");

            var popup = new BlockPickerPopup(registry, def => def.shape == BlockShape.Statement || def.shape == BlockShape.CBlock, def =>
            {
                var parent = ProgramQuery.FindNode(store.Program, stackId, parentNodeId);
                var index = parent?.branches[branchIndex].Length ?? 0;
                var location = NodeLocation.InBranch(stackId, parentNodeId, branchIndex, index);
                store.Apply(new InsertNode(location, PaletteView.InstantiatePrototype(def)));
            });

            var addButton = new Button(popup.Toggle) { text = "+ Add" };
            addButton.AddToClassList("blocky-block__add-button");

            container.Add(addButton);
            container.Add(popup);
            return container;
        }
    }
}
