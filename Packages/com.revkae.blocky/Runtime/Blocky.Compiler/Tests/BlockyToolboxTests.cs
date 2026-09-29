using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Compiler.Tests
{
    /// <summary>A level's toolbox: which blocks the palette offers, and the block limit ("solve it in 5 blocks").</summary>
    public class BlockyToolboxTests
    {
        private static BlockDefinition Def(string blockType, BlockCategory category, BlockShape shape = BlockShape.Statement)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.category = category;
            def.shape = shape;
            return def;
        }

        private static readonly BlockDefinition Move = Def("motion.move_forward", BlockCategory.Motion);
        private static readonly BlockDefinition Turn = Def("motion.turn_direction", BlockCategory.Motion);
        private static readonly BlockDefinition Repeat = Def("control.repeat", BlockCategory.Control, BlockShape.CBlock);
        private static readonly BlockDefinition Wait = Def("control.wait", BlockCategory.Control);
        private static readonly BlockDefinition WhenGo = Def("event.when_go_clicked", BlockCategory.Event, BlockShape.Trigger);

        private static BlockyToolbox Toolbox(BlockyToolbox.Show show, BlockCategory[] categories, BlockDefinition[] blocks, int limit = 0)
        {
            var toolbox = ScriptableObject.CreateInstance<BlockyToolbox>();
            toolbox.show = show;
            toolbox.categories = categories;
            toolbox.blocks = blocks;
            toolbox.blockLimit = limit;
            return toolbox;
        }

        [Test]
        public void OnlyThese_OffersTheListedCategoriesAndBlocks_AndNothingElse()
        {
            var toolbox = Toolbox(BlockyToolbox.Show.OnlyThese, new[] { BlockCategory.Motion }, new[] { Repeat, WhenGo });

            Assert.IsTrue(toolbox.Allows(Move));
            Assert.IsTrue(toolbox.Allows(Turn));
            Assert.IsTrue(toolbox.Allows(Repeat));
            Assert.IsTrue(toolbox.Allows(WhenGo));
            Assert.IsFalse(toolbox.Allows(Wait), "the rest of Control isn't listed");
        }

        [Test]
        public void AllExceptThese_HidesTheListed_AndOffersTheRest()
        {
            var toolbox = Toolbox(BlockyToolbox.Show.AllExceptThese, new[] { BlockCategory.Control }, new[] { Turn });

            Assert.IsTrue(toolbox.Allows(Move));
            Assert.IsTrue(toolbox.Allows(WhenGo));
            Assert.IsFalse(toolbox.Allows(Turn));
            Assert.IsFalse(toolbox.Allows(Repeat));
        }

        [Test]
        public void ABlockIsMatchedByItsType_NotByWhichCopyOfTheAssetItIs()
        {
            var toolbox = Toolbox(BlockyToolbox.Show.OnlyThese, new BlockCategory[0], new[] { Move });

            Assert.IsTrue(toolbox.Allows(Def("motion.move_forward", BlockCategory.Motion)));
        }

        // ---- the limit -------------------------------------------------------------------------------

        private static BlockParam Slot(string key, BlockNode block) => new() { key = key, kind = ParamKind.Text, reporter = block };
        private static BlockNode Node(string type, params BlockParam[] parameters) => new() { id = type, blockType = type, parameters = parameters };

        private static ObjectProgram Program()
        {
            // when Go clicked: repeat { move (join (a) (b)) } · and a loose "turn" lying on the table
            var join = Node("operator.join", Slot("a", Node("operator.round")), Slot("b", null));
            var move = Node("motion.move_forward", Slot("distance", join));
            var repeat = new BlockNode { id = "repeat", blockType = "control.repeat", parameters = new BlockParam[0], branches = new[] { new[] { move } } };
            return new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack { id = "stk_go", triggerBlockType = "event.when_go_clicked", sequence = new[] { repeat } },
                    new BlockStack { id = "stk_loose", triggerBlockType = "", sequence = new[] { Node("motion.turn_direction") } }
                }
            };
        }

        [Test]
        public void TheCount_IncludesBlocksInsideCBlocksAndInputs_AndLooseOnes_ButNotHats()
        {
            // repeat, move, join, round, turn — the hat doesn't count, and the empty input "b" holds nothing
            Assert.AreEqual(5, ProgramQuery.CountBlocks(Program()));
        }

        [Test]
        public void UnderTheLimit_AnyBlockMayBeAdded_AtItOnlyHats()
        {
            var roomy = Toolbox(BlockyToolbox.Show.AllExceptThese, new BlockCategory[0], new BlockDefinition[0], limit: 6);
            var full = Toolbox(BlockyToolbox.Show.AllExceptThese, new BlockCategory[0], new BlockDefinition[0], limit: 5);

            Assert.IsTrue(roomy.CanAdd(Program(), Move));
            Assert.IsFalse(full.CanAdd(Program(), Move));
            Assert.IsTrue(full.CanAdd(Program(), WhenGo), "a hat isn't a block of the solution");
        }

        [Test]
        public void NoLimit_MeansEveryBlockMayBeAdded()
        {
            var toolbox = Toolbox(BlockyToolbox.Show.AllExceptThese, new BlockCategory[0], new BlockDefinition[0]);

            Assert.IsFalse(toolbox.HasLimit);
            Assert.IsTrue(toolbox.CanAdd(Program(), Move));
        }
    }
}
