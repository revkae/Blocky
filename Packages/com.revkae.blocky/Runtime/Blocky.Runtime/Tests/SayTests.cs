using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>The say/think bubble: what the block does to the thread, and what ends up over the object's head.</summary>
    public class SayTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("Talker");

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            if (_camera != null) Object.DestroyImmediate(_camera);
            BlockySpeechBubble.CameraForTests = null;
            BlockySpeechBubble.ReadableDistance = float.PositiveInfinity;
            BlockyRuntime.Reset();
        }

        private GameObject _camera;

        /// <summary>
        /// The camera the bubble faces and measures its distance from. Handed over through the test seam rather than
        /// tagged MainCamera: the scene open in the Editor may have a main camera of its own, and which of two wins is not ours to say.
        /// </summary>
        private void PutCameraAt(Vector3 position)
        {
            _camera = new GameObject("SayTestsCamera");
            _camera.transform.position = position;
            BlockySpeechBubble.CameraForTests = _camera.AddComponent<Camera>();
        }

        private Transform Bubble() => _target.GetComponentInChildren<TextMesh>().transform.parent;

        private static BlockDefinition Def(string blockType, BlockShape shape, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static ParamSpec Style() => new()
        {
            key = "style",
            kind = ParamKind.Choice,
            defaultText = "say",
            choices = new[]
            {
                new ChoiceEntry { stableId = "say", displayNameKey = "say" },
                new ChoiceEntry { stableId = "think", displayNameKey = "think" }
            }
        };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("motion.move_forward", BlockShape.Statement, new[]
            {
                new ParamSpec { key = "distance", kind = ParamKind.Number, min = -1000, max = 1000 },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
            }),
            Def("looks.say", BlockShape.Statement, new[]
            {
                new ParamSpec { key = "message", kind = ParamKind.Text },
                new ParamSpec { key = "seconds", kind = ParamKind.Number, min = 0, max = 1000 },
                Style()
            })
        });

        private static int _nextId;

        private static BlockNode Say(string message, float seconds, string style = "say") => new()
        {
            id = "say" + ++_nextId,
            blockType = "looks.say",
            parameters = new[]
            {
                new BlockParam { key = "message", kind = ParamKind.Text, text = message },
                new BlockParam { key = "seconds", kind = ParamKind.Number, number = seconds },
                new BlockParam { key = "style", kind = ParamKind.Choice, text = style }
            }
        };

        private static BlockNode Move() => new()
        {
            id = "mv" + ++_nextId,
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = 1f },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = 0f }
            }
        };

        private VmScheduler Start(params BlockNode[] sequence)
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = sequence } }
            };
            var result = ProgramCompiler.Link(program, registry);
            CollectionAssert.IsEmpty(result.Diagnostics);

            var assembly = typeof(OpTableBuilder).Assembly;
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[0]);
            return scheduler;
        }

        /// <summary>What is actually on screen over the object, or null when there is no bubble showing.</summary>
        private string VisibleBubbleText()
        {
            var bubble = _target.GetComponentInChildren<TextMesh>(includeInactive: false);
            return bubble != null && bubble.gameObject.activeInHierarchy ? bubble.text : null;
        }

        [Test]
        public void SayWithNoDuration_PutsTheBubbleUp_AndCarriesStraightOn()
        {
            var scheduler = Start(Say("Hello!", 0f), Move());

            scheduler.Tick(1f);

            Assert.AreEqual("Hello!", VisibleBubbleText());
            Assert.AreEqual(1f, _target.transform.position.z, 1e-4f, "the next block ran in the same tick");
        }

        [Test]
        public void SayForSeconds_HoldsTheScript_ThenClearsTheBubble()
        {
            var scheduler = Start(Say("Wait for it", 2f), Move());

            scheduler.Tick(1f);
            Assert.AreEqual("Wait for it", VisibleBubbleText());
            Assert.AreEqual(0f, _target.transform.position.z, 1e-4f, "the script is held while the bubble is up");

            scheduler.Tick(1.5f); // the clock is now at 2.5s — still short of the wake time
            Assert.AreEqual("Wait for it", VisibleBubbleText(), "and it is still up until the two seconds are really gone");

            scheduler.Tick(1f); // 3.5s: past it

            Assert.IsNull(VisibleBubbleText(), "the bubble clears itself");
            Assert.AreEqual(1f, _target.transform.position.z, 1e-4f, "and the script carries on");
        }

        [Test]
        public void SayingNothing_ClearsTheBubble()
        {
            var scheduler = Start(Say("Hello!", 0f), Say(string.Empty, 0f));

            scheduler.Tick(1f);

            Assert.IsNull(VisibleBubbleText());
        }

        [Test]
        public void ThinkingShowsTheSameTextInADifferentStyle()
        {
            var scheduler = Start(Say("Hmm", 0f, "think"));

            scheduler.Tick(1f);

            var bubble = _target.GetComponentInChildren<TextMesh>();
            Assert.AreEqual("Hmm", bubble.text);
            Assert.AreEqual(FontStyle.Italic, bubble.fontStyle, "thinking is the same bubble, told apart by its style");
        }

        [Test]
        public void TheBubbleSitsAboveTheObject_AndIsNotSomethingToBumpInto()
        {
            var scheduler = Start(Say("Up here", 0f));

            scheduler.Tick(1f);

            var bubble = _target.GetComponentInChildren<TextMesh>().transform.parent;
            Assert.Greater(bubble.localPosition.y, 0f);
            Assert.IsNull(bubble.GetComponentInChildren<Collider>(), "the bubble's backing quad has no collider");
        }

        [Test]
        public void TheBubbleIsTheSameSize_WhateverTheObjectsScale()
        {
            // Imported models come at every scale: an elephant at 0.03, a car at 4. The bubble hangs under the object
            // and would shrink to nothing, or grow to a billboard, with it.
            _target.transform.localScale = new Vector3(4f, 4f, 4f);
            BlockySpeechBubble.Show(_target, "Big", think: false);
            Assert.AreEqual(1f, Bubble().lossyScale.x, 1e-3f);
            Assert.AreEqual(1f, Bubble().lossyScale.y, 1e-3f);

            _target.transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
            BlockySpeechBubble.Show(_target, "Small", think: false);
            Assert.AreEqual(1f, Bubble().lossyScale.x, 1e-3f);
            Assert.AreEqual(1f, Bubble().lossyScale.y, 1e-3f);
        }

        [Test]
        public void TheBubbleClearsEverythingTheObjectShows_NotJustItsRoot()
        {
            // A model whose root has no renderer and whose pivot is at its feet: its mesh stands well above the pivot.
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(_target.transform, false);
            mesh.transform.localPosition = new Vector3(0f, 3f, 0f);
            _target.transform.localScale = new Vector3(2f, 2f, 2f); // the mesh's top is now at y = 7

            BlockySpeechBubble.Show(_target, "Up here", think: false);

            var top = mesh.GetComponent<Renderer>().bounds.max.y;
            Assert.AreEqual(7f, top, 1e-3f);
            Assert.Greater(Bubble().position.y, top, "the bubble is above the top of the mesh, not inside it");
        }

        [Test]
        public void TheBubbleFollowsTheObject_WhenItMoves()
        {
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(_target.transform, false);
            BlockySpeechBubble.Show(_target, "Coming along", think: false);
            var before = Bubble().position;

            _target.transform.position += new Vector3(5f, 0f, 0f);

            Assert.AreEqual(before.x + 5f, Bubble().position.x, 1e-3f, "it is the object's child, so it never lags a frame behind");
        }

        [Test]
        public void ALongMessageWraps_AtItsSpaces()
        {
            const string sentence = "The quick brown fox jumps over the lazy dog and keeps on running through the park";

            var wrapped = BlockySpeechBubble.Wrap(sentence);

            StringAssert.Contains("\n", wrapped);
            foreach (var line in wrapped.Split('\n'))
                Assert.LessOrEqual(line.Length, BlockySpeechBubble.MaxLineLength, $"'{line}' is longer than a bubble line");
            Assert.AreEqual(sentence, wrapped.Replace('\n', ' '), "nothing but the line breaks changed");
        }

        [Test]
        public void Wrap_LeavesShortMessagesAndLongWordsAlone_AndKeepsLineBreaks()
        {
            Assert.AreEqual("Hello!", BlockySpeechBubble.Wrap("Hello!"));
            Assert.AreEqual("Supercalifragilisticexpialidocious", BlockySpeechBubble.Wrap("Supercalifragilisticexpialidocious", 10));
            Assert.AreEqual("one two\nthree\nfour", BlockySpeechBubble.Wrap("one two three\nfour", 8));
            Assert.AreEqual(string.Empty, BlockySpeechBubble.Wrap(string.Empty));
        }

        [Test]
        public void TheBubbleOnScreen_IsTheWrappedMessage()
        {
            const string sentence = "one two three four five six seven eight nine ten eleven";
            var scheduler = Start(Say(sentence, 0f));

            scheduler.Tick(1f);

            Assert.AreEqual(BlockySpeechBubble.Wrap(sentence), VisibleBubbleText());
        }

        [Test]
        public void TheBubbleIsDrawnWithBlockysOwnShaders_WhichEveryPipelineDraws()
        {
            // A render pipeline's own unlit shader is the wrong thing to reach for: a project can have the URP package
            // installed and render with the built-in pipeline, where URP's shaders exist and draw as the magenta error color.
            BlockySpeechBubble.Show(_target, "Hello!", think: false);

            var back = Bubble().Find("Back").GetComponent<Renderer>().sharedMaterial.shader;
            var text = Bubble().Find("Text").GetComponent<Renderer>().sharedMaterial.shader;

            Assert.AreEqual("Hidden/Blocky/Bubble Back", back.name);
            Assert.AreEqual("Hidden/Blocky/Bubble Text", text.name, "depth tested: Unity's font shader draws 3D text through walls");
            Assert.IsTrue(back.isSupported && text.isSupported);
        }

        [Test]
        public void TheBubbleLiesFlatOnTheScreen_WhereverItIs()
        {
            // Off to one side of a camera that looks down at an angle: a bubble turned towards where the camera stands
            // would be seen at a slant there.
            _target.transform.position = new Vector3(30f, 0f, 20f);
            PutCameraAt(new Vector3(0f, 5f, -10f));
            _camera.transform.rotation = Quaternion.Euler(20f, 15f, 0f);

            BlockySpeechBubble.Show(_target, "Flat", think: false);

            Assert.AreEqual(0f, Quaternion.Angle(_camera.transform.rotation, Bubble().rotation), 1e-3f);
        }

        [Test]
        public void ByDefault_ABubbleIsItsRealSize_HoweverFarTheCameraIs()
        {
            PutCameraAt(new Vector3(0f, 0f, -100f));

            BlockySpeechBubble.Show(_target, "Far away", think: false);

            Assert.AreEqual(1f, Bubble().lossyScale.x, 1e-3f);
        }

        [Test]
        public void WithAReadableDistance_AFarBubbleGrows_AndANearOneKeepsItsSize()
        {
            BlockySpeechBubble.ReadableDistance = 10f;
            _target.transform.position = new Vector3(0f, -0.5f, 0f); // the top of an object with nothing to show: the origin
            PutCameraAt(new Vector3(0f, 0f, -40f));

            BlockySpeechBubble.Show(_target, "Four times as far", think: false);
            Assert.AreEqual(4f, Bubble().lossyScale.x, 1e-2f, "at four times the distance it is four times the size: the same on screen");

            _camera.transform.position = new Vector3(0f, 0f, -5f);
            BlockySpeechBubble.Show(_target, "Close by", think: false);
            Assert.AreEqual(1f, Bubble().lossyScale.x, 1e-3f, "nearer than the readable distance it is never smaller than its real size");
        }

        [Test]
        public void Stop_ClearsEveryBubble()
        {
            var scheduler = Start(Say("Still talking", 0f));
            scheduler.Tick(1f);
            Assert.IsNotNull(VisibleBubbleText());

            BlockySpeechBubble.HideAll();

            Assert.IsNull(VisibleBubbleText());
        }
    }
}
