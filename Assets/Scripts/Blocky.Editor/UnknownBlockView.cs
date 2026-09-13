using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// A block whose <c>blockType</c> no longer resolves in the registry (asset removed after a save) still
    /// shows up and preserves its serialized params instead of silently dropping data (TDD §10.2).
    /// </summary>
    public sealed class UnknownBlockView : VisualElement
    {
        public string NodeId { get; }

        public UnknownBlockView(BlockNode node)
        {
            NodeId = node.id;
            AddToClassList("blocky-block");
            AddToClassList("blocky-block--error");

            Add(new Label($"Unknown block type: {node.blockType}"));
            foreach (var param in node.parameters)
                Add(new Label($"{param.key} = {DescribeValue(param)}"));
        }

        private static string DescribeValue(BlockParam param) => param.kind switch
        {
            ParamKind.Number => param.number.ToString("G"),
            ParamKind.Bool => param.boolean.ToString(),
            _ => param.text ?? string.Empty
        };
    }
}
