using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor.Tests
{
    public class ProgramCanvasViewTests
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
            Def("event.when_play_clicked", BlockShape.Trigger)
        });

        [Test]
        public void RendersOneStackViewPerStack()
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", canvasPosition = new Vector2(10, 20) }
                }
            };
            var store = new ProgramStore(program);

            var canvas = new ProgramCanvasView(store, registry);

            Assert.AreEqual(1, canvas.StackViews.Count);
            Assert.IsTrue(canvas.StackViews.ContainsKey("stk_1"));
        }

        [Test]
        public void RebuildsOnStoreChange()
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram { stacks = new BlockStack[0] };
            var store = new ProgramStore(program);
            var canvas = new ProgramCanvasView(store, registry);

            Assert.AreEqual(0, canvas.StackViews.Count);

            store.Apply(new CreateStack(new BlockStack { id = "stk_new", triggerBlockType = "event.when_play_clicked" }));

            Assert.AreEqual(1, canvas.StackViews.Count);
            Assert.IsTrue(canvas.StackViews.ContainsKey("stk_new"));
        }

        [Test]
        public void TableMode_RendersLooseStacksWithoutAHat_AndHasNoButtons()
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack { id = "stk_hat", triggerBlockType = "event.when_play_clicked" },
                    new BlockStack { id = "stk_loose", triggerBlockType = "" }
                }
            };

            var canvas = new ProgramCanvasView(new ProgramStore(program), registry, CanvasMode.Table);

            Assert.IsNotNull(canvas.StackViews["stk_hat"].Hat);
            Assert.IsNull(canvas.StackViews["stk_loose"].Hat);
            Assert.AreEqual(0, canvas.Query<Button>().ToList().Count);
        }

        [Test]
        public void Selection_HighlightsTheBlock_AndFollowsItThroughARebuild()
        {
            var registry = BlockRegistry.Build(new[]
            {
                Def("event.when_play_clicked", BlockShape.Trigger),
                Def("motion.move_forward", BlockShape.Statement)
            });
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk_1",
                        triggerBlockType = "event.when_play_clicked",
                        sequence = new[] { new BlockNode { id = "n1", blockType = "motion.move_forward" } }
                    }
                }
            };
            var store = new ProgramStore(program);
            var canvas = new ProgramCanvasView(store, registry, CanvasMode.Table);

            canvas.Select("stk_1", "n1");
            Assert.IsTrue(canvas.Query<BlockView>().First().ClassListContains(BlockOutline.SelectedClass));

            store.Apply(new MoveStack("stk_1", new Vector2(50, 50))); // rebuilds every view
            Assert.IsTrue(canvas.Query<BlockView>().First().ClassListContains(BlockOutline.SelectedClass));
            Assert.AreEqual("n1", canvas.SelectedNodeId);

            canvas.ClearSelection();
            Assert.IsFalse(canvas.Query<BlockView>().First().ClassListContains(BlockOutline.SelectedClass));
        }

        [Test]
        public void MultiSelection_HighlightsEveryBlock_SurvivesARebuild_AndTogglesOneOut()
        {
            var registry = BlockRegistry.Build(new[]
            {
                Def("event.when_play_clicked", BlockShape.Trigger),
                Def("motion.move_forward", BlockShape.Statement)
            });
            var program = new ObjectProgram
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
                            new BlockNode { id = "n2", blockType = "motion.move_forward" }
                        }
                    }
                }
            };
            var store = new ProgramStore(program);
            var canvas = new ProgramCanvasView(store, registry, CanvasMode.Table);
            BlockView View(string nodeId) => canvas.Query<BlockView>().ToList().Find(v => v.NodeId == nodeId);

            canvas.Select("stk_1", "n1");
            canvas.AddToSelection("stk_1", "n2");
            canvas.AddToSelection("stk_1", null); // the hat
            Assert.AreEqual(3, canvas.Selection.Count);

            store.Apply(new MoveStack("stk_1", new Vector2(50, 50))); // rebuilds every view
            Assert.AreEqual(3, canvas.Selection.Count);
            Assert.IsTrue(View("n1").ClassListContains(BlockOutline.SelectedClass));
            Assert.IsTrue(View("n2").ClassListContains(BlockOutline.SelectedClass));
            Assert.IsTrue(canvas.StackViews["stk_1"].Hat.ClassListContains(BlockOutline.SelectedClass));

            canvas.ToggleSelected("stk_1", "n1");
            Assert.IsFalse(canvas.IsSelected("stk_1", "n1"));
            Assert.IsFalse(View("n1").ClassListContains(BlockOutline.SelectedClass));
            Assert.IsTrue(canvas.IsSelected("stk_1", "n2"));

            canvas.Select("stk_1", "n1"); // a plain select replaces the group
            Assert.AreEqual(1, canvas.Selection.Count);
            Assert.IsFalse(View("n2").ClassListContains(BlockOutline.SelectedClass));
        }

        [Test]
        public void SetViewport_OnlyInstantiatesStacksNearIt()
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack { id = "stk_near", triggerBlockType = "event.when_play_clicked", canvasPosition = new Vector2(0, 0) },
                    new BlockStack { id = "stk_far", triggerBlockType = "event.when_play_clicked", canvasPosition = new Vector2(10000, 10000) }
                }
            };
            var store = new ProgramStore(program);
            var canvas = new ProgramCanvasView(store, registry);

            canvas.SetViewport(new Rect(0, 0, 800, 600));

            Assert.AreEqual(1, canvas.StackViews.Count);
            Assert.IsTrue(canvas.StackViews.ContainsKey("stk_near"));
        }

        [Test]
        public void SetViewport_Null_RendersEverythingAgain()
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack { id = "stk_near", triggerBlockType = "event.when_play_clicked", canvasPosition = new Vector2(0, 0) },
                    new BlockStack { id = "stk_far", triggerBlockType = "event.when_play_clicked", canvasPosition = new Vector2(10000, 10000) }
                }
            };
            var store = new ProgramStore(program);
            var canvas = new ProgramCanvasView(store, registry);

            canvas.SetViewport(new Rect(0, 0, 800, 600));
            Assert.AreEqual(1, canvas.StackViews.Count);

            canvas.SetViewport(null);
            Assert.AreEqual(2, canvas.StackViews.Count);
        }

        private static (ProgramStore store, ProgramCanvasView canvas) OneBlockScript()
        {
            var registry = BlockRegistry.Build(new[]
            {
                Def("event.when_play_clicked", BlockShape.Trigger),
                Def("motion.move_forward", BlockShape.Statement)
            });
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk_1",
                        triggerBlockType = "event.when_play_clicked",
                        sequence = new[] { new BlockNode { id = "n1", blockType = "motion.move_forward" } }
                    }
                }
            };
            var store = new ProgramStore(program);
            return (store, new ProgramCanvasView(store, registry, CanvasMode.Table));
        }

        [Test]
        public void SetRunning_LightsTheBlock_KeepsItThroughARebuild_AndClears()
        {
            var (store, canvas) = OneBlockScript();

            canvas.SetRunning(new[] { "n1" });
            Assert.IsTrue(canvas.Query<BlockView>().First().ClassListContains(BlockOutline.RunningClass));

            store.Apply(new MoveStack("stk_1", new Vector2(50, 50))); // rebuilds every view
            Assert.IsTrue(canvas.Query<BlockView>().First().ClassListContains(BlockOutline.RunningClass));

            canvas.SetRunning(new string[0]);
            Assert.IsFalse(canvas.Query<BlockView>().First().ClassListContains(BlockOutline.RunningClass));
        }

        [Test]
        public void SetAdvice_PinsOneBadgePerBlock_ProblemOutranksHint_AndReplacesOldBadges()
        {
            var (_, canvas) = OneBlockScript();

            canvas.SetAdvice(new[]
            {
                new Advice(AdviceKind.Hint, "stk_1", null, "hat hint"),
                new Advice(AdviceKind.Hint, "stk_1", "n1", "block hint"),
                new Advice(AdviceKind.Problem, "stk_1", "n1", "block problem")
            });

            var hatBadge = canvas.StackViews["stk_1"].Hat.Q<Label>(className: ProgramCanvasView.AdviceBadgeClass);
            Assert.AreEqual("?", hatBadge.text);
            var blockBadges = canvas.Query<BlockView>().First().Query<Label>(className: ProgramCanvasView.AdviceBadgeClass).ToList();
            Assert.AreEqual(1, blockBadges.Count);
            Assert.AreEqual("!", blockBadges[0].text);

            canvas.SetAdvice(new Advice[0]);
            Assert.AreEqual(0, canvas.Query<Label>(className: ProgramCanvasView.AdviceBadgeClass).ToList().Count);
        }
    }
}
