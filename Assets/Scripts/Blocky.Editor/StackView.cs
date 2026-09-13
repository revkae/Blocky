using System;
using Blocky.Compiler;
using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Renders one trigger-headed stack: the trigger header plus its top-level sequence. Read-only unless a
    /// <see cref="ProgramStore"/> is supplied, in which case fields become live and delete/add affordances appear.
    /// </summary>
    public sealed class StackView : VisualElement
    {
        public string StackId { get; }

        /// <summary>The top-level sequence container — used by <see cref="DropCandidateBuilder"/>.</summary>
        public VisualElement SequenceContainer { get; }

        public StackView(BlockStack stack, BlockRegistry registry, ProgramStore store = null)
        {
            StackId = stack.id;
            AddToClassList("blocky-stack");

            var triggerDef = registry.Find(stack.triggerBlockType);
            var header = new VisualElement();
            header.AddToClassList("blocky-block");
            header.AddToClassList("blocky-block--shape-trigger");
            header.Add(new Label(triggerDef != null
                ? (string.IsNullOrEmpty(triggerDef.displayNameKey) ? triggerDef.blockType : triggerDef.displayNameKey)
                : $"Unknown trigger: {stack.triggerBlockType}"));

            if (triggerDef != null)
                foreach (var spec in triggerDef.parameters)
                {
                    var value = Array.Find(stack.triggerParameters, p => p.key == spec.key);
                    if (store != null)
                    {
                        header.Add(ParamFieldFactory.CreateLiveField(spec, value, newValue =>
                            store.Apply(new SetParam(new ParamTarget(stack.id, null), spec.key, newValue))));
                    }
                    else
                    {
                        header.Add(ParamFieldFactory.CreateReadOnlyField(spec, value));
                    }
                }

            if (store != null)
            {
                var deleteButton = new Button(() => store.Apply(new DeleteStack(stack.id))) { text = "✕ Delete Stack" };
                deleteButton.AddToClassList("blocky-stack__delete");
                header.Add(deleteButton);
            }

            Add(header);

            SequenceContainer = new VisualElement();
            SequenceContainer.AddToClassList("blocky-stack__sequence");
            foreach (var node in stack.sequence)
                SequenceContainer.Add(BlockView.Create(node, registry, stack.id, store));

            if (store != null)
            {
                var popup = new BlockPickerPopup(registry, def => def.shape == BlockShape.Statement || def.shape == BlockShape.CBlock, def =>
                {
                    var current = ProgramQuery.FindStack(store.Program, stack.id);
                    var index = current?.sequence.Length ?? 0;
                    store.Apply(new InsertNode(NodeLocation.InStack(stack.id, index), PaletteView.InstantiatePrototype(def)));
                });
                var addButton = new Button(popup.Toggle) { text = "+ Add" };
                addButton.AddToClassList("blocky-block__add-button");
                var addContainer = new VisualElement();
                addContainer.AddToClassList("blocky-block__add-container");
                addContainer.Add(addButton);
                addContainer.Add(popup);
                SequenceContainer.Add(addContainer);
            }

            Add(SequenceContainer);
        }
    }
}
