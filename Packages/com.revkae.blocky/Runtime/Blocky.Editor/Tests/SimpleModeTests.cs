using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor.Tests
{
    /// <summary>
    /// <see cref="WorkspaceMode.Simple"/>: the strict single column. Three rules make it strict — a chain that
    /// snapped to nothing joins a script instead of lying loose (<see cref="SimpleDropPolicy"/>), stacks are laid
    /// out in one flowing column instead of at their stored positions, and every block owns a numbered row that
    /// can be clicked to pick it and dropped on to put a block above it, below it or in its place
    /// (<see cref="RowTargetResolver"/>).
    /// </summary>
    public class SimpleModeTests
    {
        private static BlockDefinition Def(string blockType, BlockShape shape)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.shape = shape;
            return def;
        }

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("motion.move_forward", BlockShape.Statement),
            Def("condition.true", BlockShape.Boolean)
        });

        private static BlockNode Node(string id, string blockType = "motion.move_forward") => new() { id = id, blockType = blockType };

        private static ObjectProgram OneScript() => new()
        {
            stacks = new[]
            {
                new BlockStack
                {
                    id = "stk_1",
                    triggerBlockType = "event.when_play_clicked",
                    canvasPosition = new Vector2(10, 20),
                    sequence = new[] { Node("n1"), Node("n2") }
                }
            }
        };

        [Test]
        public void ADroppedBlockThatSnappedToNothing_JoinsTheEndOfTheScript_InsteadOfLyingLoose()
        {
            var program = OneScript();

            var target = SimpleDropPolicy.Fallback(program, default, "stk_1");

            Assert.IsTrue(target.HasValue);
            Assert.AreEqual(ChainTargetKind.Insert, target.Value.Kind);
            Assert.AreEqual("stk_1", target.Value.InsertAt.StackId);
            Assert.AreEqual(2, target.Value.InsertAt.Index); // after the two blocks already there
            Assert.IsNull(target.Value.InsertAt.ParentNodeId);
        }

        [Test]
        public void WithNoScriptToJoin_TheFirstBlockStartsTheColumn()
        {
            var target = SimpleDropPolicy.Fallback(new ObjectProgram(), default, null);

            Assert.IsTrue(target.HasValue);
            Assert.AreEqual(ChainTargetKind.Free, target.Value.Kind);
        }

        [Test]
        public void AnUnknownNearestScript_FallsBackToTheLastOne()
        {
            var program = OneScript();

            var target = SimpleDropPolicy.Fallback(program, default, nearestStackId: null);

            Assert.AreEqual(ChainTargetKind.Insert, target.Value.Kind);
            Assert.AreEqual("stk_1", target.Value.InsertAt.StackId);
        }

        /// <summary>A hat can't be inserted into a script, so it starts another one — clear of the scripts already there.</summary>
        [Test]
        public void AHat_StartsAnotherScript_AtAPositionClearOfTheOthers()
        {
            var program = OneScript();
            var hat = ChainShape.Of(Def("event.when_play_clicked", BlockShape.Trigger));

            var target = SimpleDropPolicy.Fallback(program, hat, "stk_1");

            Assert.AreEqual(ChainTargetKind.Free, target.Value.Kind);
            Assert.AreNotEqual(program.stacks[0].canvasPosition, target.Value.Position);
        }

        /// <summary>A condition only fits a hexagonal hole, and this mode has nowhere loose to put one.</summary>
        [Test]
        public void ACondition_DroppedOutsideAHole_IsRefused_SoItGoesBack()
        {
            var target = SimpleDropPolicy.Fallback(OneScript(), ChainShape.Condition, "stk_1");

            Assert.IsNull(target);
        }

        [Test]
        public void TheCanvas_LaysScriptsOutInAColumn_AndNumbersTheirBlocks()
        {
            var program = OneScript();
            program.stacks[0].canvasPosition = new Vector2(400, 300); // deliberately far from the column
            var canvas = new ProgramCanvasView(new ProgramStore(program), BuildRegistry(), CanvasMode.Table)
            {
                Mode = WorkspaceMode.Simple
            };

            var stackView = canvas.StackViews["stk_1"];
            Assert.AreEqual(Position.Relative, stackView.resolvedStyle.position, "a column flows; it doesn't place stacks at stored positions");
            Assert.IsTrue(canvas.ClassListContains(ProgramCanvasView.SimpleClass));
            Assert.IsNotNull(UQueryExtensions.Q(canvas, null, ProgramCanvasView.RowLayerClass), "the numbered rows are ruled behind the blocks");
        }

        // ─── The three places a row offers ──────────────────────────────────────────────────────────────

        private static RowTarget Row(float top = 0f, float height = 100f, string nodeId = "n1", int index = 0,
            bool hasFollowers = false, bool isTerminal = false) =>
            new(NodeLocation.InStack("stk_1", index), "stk_1", nodeId, new Rect(0f, top, 200f, height), hasFollowers, isTerminal);

        private static RowIntent IntentAt(RowTarget row, float y, bool endsWithCap = false)
        {
            Assert.IsTrue(RowTargetResolver.TryFind(new[] { row }, new Vector2(10f, y), endsWithCap, out _, out var intent));
            return intent;
        }

        [Test]
        public void TheTopOfARow_MeansAboveIt_TheBottomMeansBelow_AndTheMiddleMeansInItsPlace()
        {
            var row = Row(top: 0f, height: 100f);

            Assert.AreEqual(RowIntent.Above, IntentAt(row, 4f));
            Assert.AreEqual(RowIntent.Replace, IntentAt(row, 50f));
            Assert.AreEqual(RowIntent.Below, IntentAt(row, 96f));
        }

        /// <summary>Otherwise a C-block holding half the script would turn most of the table into "above".</summary>
        [Test]
        public void OnATallRow_TheEdgeZoneStopsGrowing()
        {
            var row = Row(top: 0f, height: 600f);

            Assert.AreEqual(RowIntent.Above, IntentAt(row, RowTargetResolver.MaxEdgePixels - 1f));
            Assert.AreEqual(RowIntent.Replace, IntentAt(row, RowTargetResolver.MaxEdgePixels + 1f));
        }

        [Test]
        public void ADropOffEveryRow_FindsNothing_AndFallsBackToTheEndOfTheScript()
        {
            Assert.IsFalse(RowTargetResolver.TryFind(new[] { Row() }, new Vector2(10f, 400f), false, out _, out _));
        }

        /// <summary>A block nested in a C-block's mouth is inside the C-block's own row: the smaller one is what's aimed at.</summary>
        [Test]
        public void TheInnermostRowUnderThePointer_Wins()
        {
            var outer = Row(top: 0f, height: 300f, nodeId: "outer");
            var inner = Row(top: 60f, height: 40f, nodeId: "inner");

            Assert.IsTrue(RowTargetResolver.TryFind(new[] { outer, inner }, new Vector2(10f, 80f), false, out var row, out _));
            Assert.AreEqual("inner", row.NodeId);
        }

        [Test]
        public void AHatRow_OnlyEverTakesABlockUnderIt()
        {
            var hat = new RowTarget(NodeLocation.InStack("stk_1", 0), "stk_1", null, new Rect(0f, 0f, 200f, 60f), false, false);

            Assert.AreEqual(RowIntent.Below, IntentAt(hat, 2f), "there is no above a hat, and nothing replaces one");
            Assert.AreEqual(RowIntent.Below, IntentAt(hat, 30f));
        }

        /// <summary>A cap ends a script: it can't be put in front of blocks that already follow, wherever on the row it lands.</summary>
        [Test]
        public void AChainEndingInACap_IsOnlyOfferedThePlacesThatLeaveNothingStranded()
        {
            var row = Row(top: 0f, height: 100f, hasFollowers: true);

            Assert.IsFalse(row.Allows(RowIntent.Above, chainEndsWithCap: true));
            Assert.IsFalse(row.Allows(RowIntent.Below, chainEndsWithCap: true));
            Assert.IsFalse(RowTargetResolver.TryFind(new[] { row }, new Vector2(10f, 50f), true, out _, out _),
                "nothing it could do to this row is legal, so the drop falls back to the end of the script");
        }

        [Test]
        public void NothingGoesUnderACap()
        {
            var row = Row(isTerminal: true);

            Assert.IsFalse(row.Allows(RowIntent.Below, chainEndsWithCap: false));
            Assert.AreEqual(RowIntent.Above, IntentAt(row, 95f), "aiming below a cap falls back to above it");
        }

        [Test]
        public void AboveUsesTheBlocksOwnSlot_BelowTheNextOne_AndReplaceTakesItsPlace()
        {
            var row = Row(index: 2);

            var above = row.Resolve(RowIntent.Above);
            Assert.AreEqual(ChainTargetKind.Insert, above.Kind);
            Assert.AreEqual(2, above.InsertAt.Index);

            var below = row.Resolve(RowIntent.Below);
            Assert.AreEqual(ChainTargetKind.Insert, below.Kind);
            Assert.AreEqual(3, below.InsertAt.Index);

            var replace = row.Resolve(RowIntent.Replace);
            Assert.AreEqual(ChainTargetKind.Replace, replace.Kind);
            Assert.AreEqual(2, replace.InsertAt.Index);
            Assert.AreEqual("stk_1", replace.InsertAt.StackId);
        }

        [Test]
        public void EachBlockGetsARow_ThatCarriesTheBlockItStandsFor()
        {
            var program = OneScript();
            var canvas = new ProgramCanvasView(new ProgramStore(program), BuildRegistry(), CanvasMode.Table)
            {
                Mode = WorkspaceMode.Simple
            };

            var rows = new List<RowTarget>();
            RowTargetCollector.Collect(canvas, rows);

            Assert.AreEqual(3, rows.Count, "the hat and its two blocks");
            Assert.IsTrue(rows[0].IsHat);
            Assert.AreEqual("n1", rows[1].NodeId);
            Assert.AreEqual(0, rows[1].At.Index);
            Assert.IsTrue(rows[1].HasFollowers);
            Assert.AreEqual("n2", rows[2].NodeId);
            Assert.AreEqual(1, rows[2].At.Index);
            Assert.IsFalse(rows[2].HasFollowers);
        }

        [Test]
        public void TheCanvas_GoesBackToStoredPositions_InFreeMode()
        {
            var program = OneScript();
            var canvas = new ProgramCanvasView(new ProgramStore(program), BuildRegistry(), CanvasMode.Table)
            {
                Mode = WorkspaceMode.Simple
            };

            canvas.Mode = WorkspaceMode.Free;

            var stackView = canvas.StackViews["stk_1"];
            Assert.AreEqual(Position.Absolute, stackView.resolvedStyle.position);
            // The inline style, not resolvedStyle: lengths only resolve once the element is in a laid-out panel.
            Assert.AreEqual(10f, stackView.style.left.value.value, 1e-3f);
            Assert.AreEqual(20f, stackView.style.top.value.value, 1e-3f);
            Assert.IsFalse(canvas.ClassListContains(ProgramCanvasView.SimpleClass));
            Assert.IsNull(UQueryExtensions.Q(canvas, null, ProgramCanvasView.RowLayerClass));
        }
    }
}
