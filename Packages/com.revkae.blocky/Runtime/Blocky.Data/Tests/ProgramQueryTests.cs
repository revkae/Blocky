using NUnit.Framework;

namespace Blocky.Data.Tests
{
    public class ProgramQueryTests
    {
        private static ObjectProgram SampleProgram() => new()
        {
            stacks = new[]
            {
                new BlockStack
                {
                    id = "stk_1",
                    triggerBlockType = "event.when_play_clicked",
                    sequence = new[]
                    {
                        new BlockNode { id = "n1", blockType = "motion.move_forward" },
                        new BlockNode
                        {
                            id = "n2",
                            blockType = "control.repeat",
                            branches = new[] { new[] { new BlockNode { id = "n3", blockType = "motion.move_forward" } } }
                        }
                    }
                }
            }
        };

        [Test]
        public void FindLocation_TopLevelNode_ReturnsStackIndex()
        {
            var location = ProgramQuery.FindLocation(SampleProgram(), "stk_1", "n2");

            Assert.IsNotNull(location);
            Assert.AreEqual("stk_1", location.Value.StackId);
            Assert.IsNull(location.Value.ParentNodeId);
            Assert.AreEqual(1, location.Value.Index);
        }

        [Test]
        public void FindLocation_NestedNode_ReturnsParentAndBranch()
        {
            var location = ProgramQuery.FindLocation(SampleProgram(), "stk_1", "n3");

            Assert.IsNotNull(location);
            Assert.AreEqual("n2", location.Value.ParentNodeId);
            Assert.AreEqual(0, location.Value.BranchIndex);
            Assert.AreEqual(0, location.Value.Index);
        }

        [Test]
        public void FindLocation_UnknownNode_ReturnsNull()
        {
            Assert.IsNull(ProgramQuery.FindLocation(SampleProgram(), "stk_1", "does_not_exist"));
        }
    }
}
