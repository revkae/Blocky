using Blocky.Data;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// One numbered band behind the strict column (<see cref="WorkspaceMode.Simple"/>): a line ruled above a
    /// block, its number in the gutter, and the block's whole height as a strip across the table. The strip is
    /// the block's *area* — clicking it anywhere, even far to the right of the block itself, picks that block,
    /// and a selected block tints its whole row the way a line of code is highlighted in an editor.
    /// Bands are laid out behind the blocks and in reading order, so a band nested inside a C-block's band is
    /// added after it and therefore sits on top: aiming at a nested block picks the nested block.
    /// </summary>
    public sealed class CanvasRow : VisualElement
    {
        public const string UssClassName = "blocky-row";
        public const string NumberUssClassName = "blocky-row__number";
        public const string GutterUssClassName = "blocky-row__gutter";
        public const string SelectedUssClassName = "blocky-row--selected";

        private readonly Label _number;

        /// <summary>The block this row stands for.</summary>
        public BlockRef Block { get; private set; }

        public CanvasRow()
        {
            AddToClassList(UssClassName);

            var gutter = new VisualElement { pickingMode = PickingMode.Ignore };
            gutter.AddToClassList(GutterUssClassName);
            _number = new Label { pickingMode = PickingMode.Ignore };
            _number.AddToClassList(NumberUssClassName);
            gutter.Add(_number);
            Add(gutter);
        }

        /// <summary>Re-points an existing band at a block — bands are pooled and reused on every layout pass.</summary>
        public void Set(BlockRef block, int number, float top, float height)
        {
            Block = block;
            _number.text = number.ToString();
            style.display = DisplayStyle.Flex;
            style.top = top;
            style.height = height;
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            Block = default;
        }

        /// <summary>The band a press landed on, or null when it landed on something else (a block, or empty table).</summary>
        public static CanvasRow Of(VisualElement hit)
        {
            for (var el = hit; el != null; el = el.parent)
                if (el is CanvasRow row) return row;
            return null;
        }
    }
}
