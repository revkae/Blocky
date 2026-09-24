using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    public class CatalogOpsTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("CatalogOpsTestTarget");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_target);

        private static BlockDefinition Def(string blockType, ParamSpec[] parameters) => new BlockDefinitionBuilder(blockType, parameters).Build();

        // Minimal builder so each test only states what it needs, instead of repeating ScriptableObject.CreateInstance boilerplate.
        private sealed class BlockDefinitionBuilder
        {
            private readonly BlockDefinition _def;
            public BlockDefinitionBuilder(string blockType, ParamSpec[] parameters)
            {
                _def = ScriptableObject.CreateInstance<BlockDefinition>();
                _def.blockType = blockType;
                _def.executorKey = blockType;
                _def.shape = BlockShape.Statement;
                _def.parameters = parameters;
            }
            public BlockDefinition Build() => _def;
        }

        private (VmScheduler scheduler, CompiledProgram compiled) BuildScheduler(BlockDefinition def, BlockNode node)
        {
            // A statement block can't also be its own stack trigger in real use — give the stack a throwaway
            // trigger def too, purely so Validate/Link succeed for this single-op test.
            var triggerDef = ScriptableObject.CreateInstance<BlockDefinition>();
            triggerDef.blockType = "test.trigger";
            triggerDef.shape = BlockShape.Trigger;

            var registry = BlockRegistry.Build(new[] { def, triggerDef });
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "test.trigger", sequence = new[] { node } } }
            };

            var result = ProgramCompiler.Link(program, registry);
            Assert.IsFalse(result.HasErrors);
            var opTable = OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly);
            return (new VmScheduler(opTable), result.Program);
        }

        private static BlockParam P(string key, float number) => new() { key = key, kind = ParamKind.Number, number = number };
        private static BlockParam PChoice(string key, string text) => new() { key = key, kind = ParamKind.Choice, text = text };
        private static BlockParam PText(string key, string text) => new() { key = key, kind = ParamKind.Text, text = text };

        [Test]
        public void TurnDirection_Left_Instant_TurnsLeft_AroundY()
        {
            var def = Def("motion.turn_direction", new[]
            {
                new ParamSpec { key = "direction", kind = ParamKind.Choice, choices = new[] { new ChoiceEntry { stableId = "left" }, new ChoiceEntry { stableId = "right" } } },
                new ParamSpec { key = "degrees", kind = ParamKind.Number, min = -360, max = 360 },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
            });
            var node = new BlockNode
            {
                id = "n1", blockType = "motion.turn_direction",
                parameters = new[] { PChoice("direction", "left"), P("degrees", 90f), P("duration", 0f) }
            };

            var (scheduler, compiled) = BuildScheduler(def, node);
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Tick(1f);

            // Facing +Z, a left turn faces -X: in Unity a positive yaw turns right, so left is -90 (270).
            Assert.AreEqual(270f, _target.transform.eulerAngles.y, 1e-3f);
            Assert.Less(_target.transform.forward.x, -0.99f, "it faces -X, its left");
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void SetRotation_World_IsInstant()
        {
            var def = Def("motion.set_rotation", new[]
            {
                new ParamSpec { key = "x", kind = ParamKind.Number, min = -360, max = 360 },
                new ParamSpec { key = "y", kind = ParamKind.Number, min = -360, max = 360 },
                new ParamSpec { key = "z", kind = ParamKind.Number, min = -360, max = 360 },
                new ParamSpec { key = "space", kind = ParamKind.Choice, choices = new[] { new ChoiceEntry { stableId = "world" }, new ChoiceEntry { stableId = "local" } } }
            });
            var node = new BlockNode
            {
                id = "n1", blockType = "motion.set_rotation",
                parameters = new[] { P("x", 0f), P("y", 45f), P("z", 0f), PChoice("space", "world") }
            };

            var (scheduler, compiled) = BuildScheduler(def, node);
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Tick(1f);

            Assert.AreEqual(45f, _target.transform.eulerAngles.y, 1e-3f);
        }

        [Test]
        public void SetVisible_TogglesRendererEnabled()
        {
            var renderer = _target.AddComponent<MeshRenderer>();
            renderer.enabled = true;

            var def = Def("looks.set_visible", new[] { new ParamSpec { key = "visible", kind = ParamKind.Bool } });
            var node = new BlockNode
            {
                id = "n1", blockType = "looks.set_visible",
                parameters = new[] { new BlockParam { key = "visible", kind = ParamKind.Bool, boolean = false } }
            };

            var (scheduler, compiled) = BuildScheduler(def, node);
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Tick(1f);

            Assert.IsFalse(renderer.enabled);
        }

        [Test]
        public void SetScale_Instant_SetsLocalScale()
        {
            var def = Def("looks.set_scale", new[]
            {
                new ParamSpec { key = "x", kind = ParamKind.Number, min = 0, max = 100 },
                new ParamSpec { key = "y", kind = ParamKind.Number, min = 0, max = 100 },
                new ParamSpec { key = "z", kind = ParamKind.Number, min = 0, max = 100 },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 100 }
            });
            var node = new BlockNode
            {
                id = "n1", blockType = "looks.set_scale",
                parameters = new[] { P("x", 2f), P("y", 2f), P("z", 2f), P("duration", 0f) }
            };

            var (scheduler, compiled) = BuildScheduler(def, node);
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Tick(1f);

            Assert.AreEqual(new Vector3(2, 2, 2), _target.transform.localScale);
        }

        [Test]
        public void SetScale_OverTime_ConvergesExactlyToTargetWithoutRememberingStart()
        {
            var def = Def("looks.set_scale", new[]
            {
                new ParamSpec { key = "x", kind = ParamKind.Number, min = 0, max = 100 },
                new ParamSpec { key = "y", kind = ParamKind.Number, min = 0, max = 100 },
                new ParamSpec { key = "z", kind = ParamKind.Number, min = 0, max = 100 },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 100 }
            });
            var node = new BlockNode
            {
                id = "n1", blockType = "looks.set_scale",
                parameters = new[] { P("x", 10f), P("y", 10f), P("z", 10f), P("duration", 2f) }
            };

            var (scheduler, compiled) = BuildScheduler(def, node);
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f); // halfway
            Assert.AreEqual(5.5f, _target.transform.localScale.x, 1e-2f); // started at (1,1,1): 1 + (10-1)*0.5
            Assert.AreEqual(ThreadState.YieldedFrame, thread.State);

            scheduler.Tick(1f); // finished
            Assert.AreEqual(10f, _target.transform.localScale.x, 1e-2f);
            Assert.AreEqual(ThreadState.Done, thread.State);
        }

        [Test]
        public void ChangeColor_NoRenderer_IsHarmlessNoOp()
        {
            var def = Def("looks.change_color", new[]
            {
                new ParamSpec { key = "color", kind = ParamKind.Text },
                new ParamSpec { key = "duration", kind = ParamKind.Number }
            });
            var node = new BlockNode
            {
                id = "n1", blockType = "looks.change_color",
                parameters = new[] { PText("color", "#FF0000"), P("duration", 0f) }
            };

            var (scheduler, compiled) = BuildScheduler(def, node);
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            Assert.DoesNotThrow(() => scheduler.Tick(1f));
            Assert.AreEqual(ThreadState.Done, thread.State);
        }
    }
}
