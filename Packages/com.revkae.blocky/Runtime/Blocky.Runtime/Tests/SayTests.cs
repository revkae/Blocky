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
            BlockyRuntime.Reset();
        }

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
