using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blocky.Runtime.Tests
{
    /// <summary>
    /// <c>change color</c> on a model whose meshes sit under a root with no renderer of its own (ADR-043): every
    /// renderer under the object is recolored, on a copy of its material, and Reset puts each one back.
    /// </summary>
    public class ChangeColorTests
    {
        private GameObject _model;
        private Renderer _first;
        private Renderer _second;
        private readonly List<Object> _made = new();

        [SetUp]
        public void SetUp()
        {
            _model = new GameObject("ChangeColorModel");
            _first = Part(PrimitiveType.Cube);
            _second = Part(PrimitiveType.Sphere);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var renderer in new[] { _first, _second })
                if (renderer != null && renderer.sharedMaterial != null && !_made.Contains(renderer.sharedMaterial) && IsCopy(renderer.sharedMaterial))
                    _made.Add(renderer.sharedMaterial);

            Object.DestroyImmediate(_model);
            foreach (var made in _made)
                if (made != null) Object.DestroyImmediate(made);
            _made.Clear();

            BlockyRuntime.Reset();
        }

        private Renderer Part(PrimitiveType shape)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.transform.SetParent(_model.transform, false);
            return part.GetComponent<Renderer>();
        }

        private static bool IsCopy(Material material) => material.name.EndsWith("(Instance)");

        private static BlockRegistry BuildRegistry()
        {
            var play = ScriptableObject.CreateInstance<BlockDefinition>();
            play.blockType = "event.when_play_clicked";
            play.shape = BlockShape.Trigger;

            var color = ScriptableObject.CreateInstance<BlockDefinition>();
            color.blockType = "looks.change_color";
            color.executorKey = "looks.change_color";
            color.shape = BlockShape.Statement;
            color.parameters = new[]
            {
                new ParamSpec { key = "color", kind = ParamKind.Text },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
            };

            return BlockRegistry.Build(new[] { play, color });
        }

        /// <summary>Runs "change color to <paramref name="html"/> over <paramref name="duration"/>" on <paramref name="on"/>, one second per tick.</summary>
        private static VmScheduler ChangeColor(GameObject on, string html, float duration, int ticks)
        {
            // Outside Play mode Unity logs an error each time a renderer is given its own copy of a material, because
            // nothing there will ever free it. In the game that copy is exactly what the block is for (ADR-008).
            // Set here, not in SetUp: the test runner gives SetUp a log scope of its own, and this lasts only as
            // long as the scope it is set in.
            LogAssert.ignoreFailingMessages = true;

            var registry = BuildRegistry();
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
                            new BlockNode
                            {
                                id = "n_color",
                                blockType = "looks.change_color",
                                parameters = new[]
                                {
                                    new BlockParam { key = "color", kind = ParamKind.Text, text = html },
                                    new BlockParam { key = "duration", kind = ParamKind.Number, number = duration }
                                }
                            }
                        }
                    }
                }
            };

            var compiled = ProgramCompiler.Link(program, registry).Program;
            var scheduler = new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
            scheduler.Start(compiled, on, compiled.StackEntryPoints[0]);
            for (var i = 0; i < ticks; i++) scheduler.Tick(1f);
            return scheduler;
        }

        [Test]
        public void ARootWithNoRendererOfItsOwn_HasEveryMeshUnderItRecolored_OnCopies()
        {
            var original = _first.sharedMaterial; // both primitives share the project's default material
            var originalColor = original.color;
            Assert.AreSame(original, _second.sharedMaterial);

            ChangeColor(_model, "#FF0000", 0f, 1);

            Assert.AreEqual(Color.red, _first.sharedMaterial.color);
            Assert.AreEqual(Color.red, _second.sharedMaterial.color);
            Assert.AreNotSame(original, _first.sharedMaterial, "a copy of its own was recolored…");
            Assert.AreNotSame(_first.sharedMaterial, _second.sharedMaterial, "…one for each renderer…");
            Assert.AreEqual(originalColor, original.color, "…and the shared original was left as it was");
        }

        [Test]
        public void Reset_PutsEveryMeshBackOnItsOriginalMaterial()
        {
            var original = _first.sharedMaterial;
            var originalColor = original.color;
            var snapshot = new WorldSnapshot();
            snapshot.Capture(_model);

            ChangeColor(_model, "#00FF00", 0f, 1);
            _made.Add(_first.sharedMaterial); // the copies are no longer on the renderers after Reset: free them by hand
            _made.Add(_second.sharedMaterial);
            snapshot.RestoreAll();

            Assert.AreSame(original, _first.sharedMaterial);
            Assert.AreSame(original, _second.sharedMaterial);
            Assert.AreEqual(originalColor, original.color);
        }

        [Test]
        public void OverTime_EachRendererStartsFromItsOwnColor_AndAllArriveTogether()
        {
            var black = new Material(_first.sharedMaterial) { color = Color.black };
            var white = new Material(_first.sharedMaterial) { color = Color.white };
            _made.Add(black);
            _made.Add(white);
            _first.sharedMaterial = black;
            _second.sharedMaterial = white;

            ChangeColor(_model, "#FF0000", 2f, 1); // half way

            Assert.AreEqual(0.5f, _first.sharedMaterial.color.r, 1e-3f, "from black, half way to red");
            Assert.AreEqual(0f, _first.sharedMaterial.color.g, 1e-3f);
            Assert.AreEqual(1f, _second.sharedMaterial.color.r, 1e-3f, "from white, half way to red");
            Assert.AreEqual(0.5f, _second.sharedMaterial.color.g, 1e-3f);
            Assert.AreEqual(Color.black, black.color, "the materials they started with are untouched");
            Assert.AreEqual(Color.white, white.color);
        }

        [Test]
        public void ASpeechBubble_IsNotPartOfTheObjectsLook()
        {
            BlockySpeechBubble.Show(_model, "hello", think: false);
            var bubble = _model.transform.Find("BlockyBubble/Back").GetComponent<Renderer>();
            var bubbleMaterial = bubble.sharedMaterial;
            var bubbleColor = bubbleMaterial.color;
            var snapshot = new WorldSnapshot();

            ChangeColor(_model, "#0000FF", 0f, 1);
            snapshot.Capture(_model);
            var moment = snapshot.SaveMoment();

            Assert.AreSame(bubbleMaterial, bubble.sharedMaterial, "no copy was made for the bubble");
            Assert.AreEqual(bubbleColor, bubbleMaterial.color);
            Assert.AreEqual(Color.blue, _first.sharedMaterial.color, "while the object itself changed");
            Assert.DoesNotThrow(() => snapshot.RestoreMoment(moment));
            _made.Add(bubbleMaterial);
        }
    }
}
