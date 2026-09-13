using Blocky.Compiler;
using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// One stack on the table: a <see cref="HatView"/> plus the sequence under it, or — for a loose stack — just
    /// the sequence. Read-only unless a <see cref="ProgramStore"/> is supplied; <c>buttons</c> adds the Editor
    /// window's delete/"+ Add" affordances.
    /// </summary>
    public sealed class StackView : VisualElement
    {
        public string StackId { get; }

        /// <summary>The event block heading this stack, or null for a loose stack.</summary>
        public HatView Hat { get; }

        /// <summary>The top-level sequence container — used by <see cref="DropCandidateBuilder"/> and <see cref="SnapTargetCollector"/>.</summary>
        public VisualElement SequenceContainer { get; }

        public StackView(BlockStack stack, BlockRegistry registry, ProgramStore store = null, bool buttons = true)
        {
            StackId = stack.id;
            AddToClassList("blocky-stack");

            if (ProgramQuery.IsLoose(stack))
            {
                AddToClassList("blocky-stack--loose");
            }
            else
            {
                Hat = new HatView(registry.Find(stack.triggerBlockType), stack.triggerBlockType, stack.triggerParameters, stack.id, store);
                if (store != null && buttons)
                {
                    var deleteButton = new Button(() => store.Apply(new DeleteStack(stack.id))) { text = "✕ Delete Stack" };
                    deleteButton.AddToClassList("blocky-stack__delete");
                    Hat.Header.Add(deleteButton);
                }
                Add(Hat);
            }

            SequenceContainer = new VisualElement();
            SequenceContainer.AddToClassList("blocky-stack__sequence");
            foreach (var node in stack.sequence)
                SequenceContainer.Add(BlockView.Create(node, registry, stack.id, store, buttons));

            if (store != null && buttons)
            {
                var popup = new BlockPickerPopup(registry, BlockView.IsSequenceBlock, def =>
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
