using System;
using Blocky.Compiler;

// The statics here are fixed values that never change, so there is nothing to reset between Play sessions.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Editor
{
    /// <summary>
    /// A block view on the table that can be grabbed and selected: the stack it belongs to and the node it shows
    /// (<see cref="NodeId"/> is null for a stack's hat). Every "is this a block?" check goes through this
    /// interface, so a new kind of block view only has to implement it to become draggable and selectable.
    /// </summary>
    public interface IBlockElement
    {
        string StackId { get; }
        string NodeId { get; }
    }

    /// <summary>USS class names shared by every block view — built once per category, not per view on every rebuild.</summary>
    public static class BlockClasses
    {
        private static readonly string[] CategoryClasses = BuildCategoryClasses();

        /// <summary><c>blocky-block--category-&lt;name&gt;</c>: sets the block's color (<c>--blocky-fill</c>) in blocky-*.uss.</summary>
        public static string Category(BlockCategory category) => CategoryClasses[(int)category];

        private static string[] BuildCategoryClasses()
        {
            var categories = (BlockCategory[])Enum.GetValues(typeof(BlockCategory));
            var classes = new string[categories.Length];
            foreach (var category in categories)
                classes[(int)category] = $"blocky-block--category-{category.ToString().ToLowerInvariant()}";
            return classes;
        }
    }
}
