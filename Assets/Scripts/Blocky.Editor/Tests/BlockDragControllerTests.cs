using System.Collections.Generic;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Editor.Tests
{
    public class BlockDragControllerTests
    {
        private static ProgramStore BuildStore()
        {
            var program = new ObjectProgram
            {
                targetObjectUid = "obj_1",
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk_1",
                        triggerBlockType = "event.when_play_clicked",
                        sequence = new[]
                        {
                            new BlockNode { id = "n1", blockType = "motion.move_forward" },
                            new BlockNode { id = "n2", blockType = "motion.move_forward" },
                            new BlockNode { id = "n3", blockType = "motion.move_forward" }
                        }
                    }
                }
            };
            return new ProgramStore(program);
        }

        [Test]
        public void BelowThreshold_NeverEntersDragging_AndCommitIsANoOp()
        {
            var store = BuildStore();
            var controller = new BlockDragController(store);

            controller.BeginPickNode(NodeLocation.InStack("stk_1", 0), new Vector2(0, 0));
            controller.OnPointerMove(new Vector2(1, 1), new List<DropCandidate>());
            Assert.AreEqual(DragState.Picked, controller.State);

            controller.CommitNodeDrop();

            Assert.AreEqual(DragState.Idle, controller.State);
            Assert.AreEqual(3, store.Program.stacks[0].sequence.Length);
            Assert.AreEqual("n1", store.Program.stacks[0].sequence[0].id);
        }

        [Test]
        public void DragToSequenceEnd_CommitsExactlyOneMoveNode()
        {
            var store = BuildStore();
            var controller = new BlockDragController(store);

            controller.BeginPickNode(NodeLocation.InStack("stk_1", 0), new Vector2(0, 0)); // dragging n1
            var candidates = new[] { new DropCandidate(new Rect(0, 100, 50, 10), DropCandidateKind.SequenceEnd, "stk_1", null, -1, 3, 0) };
            controller.OnPointerMove(new Vector2(10, 105), candidates);
            Assert.AreEqual(DragState.Dragging, controller.State);

            controller.CommitNodeDrop();

            Assert.AreEqual(DragState.Idle, controller.State);
            var sequence = store.Program.stacks[0].sequence;
            Assert.AreEqual("n2", sequence[0].id);
            Assert.AreEqual("n3", sequence[1].id);
            Assert.AreEqual("n1", sequence[2].id); // moved to the end
        }

        [Test]
        public void DragWithinSameContainer_AdjustsTargetIndexForRemovalShift()
        {
            // n1 (index 0) dropped at the gap currently before n3 (index 2): after n1's own removal shifts
            // everything down by one, it should land between n2 and n3, not after n3.
            var store = BuildStore();
            var controller = new BlockDragController(store);

            controller.BeginPickNode(NodeLocation.InStack("stk_1", 0), new Vector2(0, 0));
            var candidates = new[] { new DropCandidate(new Rect(0, 100, 50, 10), DropCandidateKind.SequenceGap, "stk_1", null, -1, 2, 0) };
            controller.OnPointerMove(new Vector2(10, 105), candidates);
            controller.CommitNodeDrop();

            var sequence = store.Program.stacks[0].sequence;
            Assert.AreEqual("n2", sequence[0].id);
            Assert.AreEqual("n1", sequence[1].id);
            Assert.AreEqual("n3", sequence[2].id);
        }

        [Test]
        public void NoCandidateUnderPointer_LeavesModelUntouched()
        {
            var store = BuildStore();
            var controller = new BlockDragController(store);

            controller.BeginPickNode(NodeLocation.InStack("stk_1", 0), new Vector2(0, 0));
            controller.OnPointerMove(new Vector2(500, 500), new List<DropCandidate>());
            Assert.AreEqual(DragState.Dragging, controller.State);

            controller.CommitNodeDrop();

            Assert.AreEqual(DragState.Idle, controller.State);
            Assert.AreEqual("n1", store.Program.stacks[0].sequence[0].id);
        }

        [Test]
        public void DropBeyondNestingCap_IsRejected_AndModelUntouched()
        {
            var store = BuildStore();
            var controller = new BlockDragController(store);
            var rejections = new List<string>();
            controller.OnDropRejected += reason => rejections.Add(reason);

            controller.BeginPickNode(NodeLocation.InStack("stk_1", 0), new Vector2(0, 0));
            var tooDeep = new[]
            {
                new DropCandidate(new Rect(0, 0, 10, 10), DropCandidateKind.BodyCavity, "stk_1", "n2", 0, 0, BlockDragController.MaxNestingDepth)
            };
            controller.OnPointerMove(new Vector2(5, 5), tooDeep);
            controller.CommitNodeDrop();

            Assert.AreEqual(1, rejections.Count);
            Assert.AreEqual("n1", store.Program.stacks[0].sequence[0].id);
        }

        [Test]
        public void Cancel_DuringDrag_ReturnsToIdle_WithoutTouchingModel()
        {
            var store = BuildStore();
            var controller = new BlockDragController(store);

            controller.BeginPickNode(NodeLocation.InStack("stk_1", 0), new Vector2(0, 0));
            controller.OnPointerMove(new Vector2(500, 500), new List<DropCandidate>());
            Assert.AreEqual(DragState.Dragging, controller.State);

            controller.Cancel();

            Assert.AreEqual(DragState.Idle, controller.State);
            Assert.AreEqual("n1", store.Program.stacks[0].sequence[0].id);
        }

        [Test]
        public void CommitStackDrop_NudgesAwayFromCollidingStack()
        {
            var store = BuildStore();
            var controller = new BlockDragController(store);

            controller.BeginPickStack("stk_1", new Vector2(0, 0));
            controller.OnPointerMove(new Vector2(100, 100), new List<DropCandidate>());

            controller.CommitStackDrop(new[] { new Vector2(100, 100) }, new Vector2(50, 50));

            Assert.AreEqual(DragState.Idle, controller.State);
            Assert.AreNotEqual(new Vector2(100, 100), store.Program.stacks[0].canvasPosition);
        }

        [Test]
        public void BeginPick_WhileAlreadyDragging_Throws()
        {
            var store = BuildStore();
            var controller = new BlockDragController(store);
            controller.BeginPickNode(NodeLocation.InStack("stk_1", 0), new Vector2(0, 0));

            Assert.Throws<System.InvalidOperationException>(() =>
                controller.BeginPickNode(NodeLocation.InStack("stk_1", 1), new Vector2(0, 0)));
        }
    }
}
