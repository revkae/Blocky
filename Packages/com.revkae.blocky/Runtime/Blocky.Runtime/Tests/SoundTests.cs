using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>Sound: the generated tones, the speaker each object gets, and what the blocks do to the script.</summary>
    public class SoundTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("Noisy");

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            BlockyRuntime.Reset();
        }

        // ---- tones -----------------------------------------------------------------------------------

        [Test]
        public void MiddleAIs440Hz_AndAnOctaveUpIsDouble()
        {
            Assert.AreEqual(440f, BlockyAudio.FrequencyOf(69), 0.01f);
            Assert.AreEqual(880f, BlockyAudio.FrequencyOf(81), 0.01f);
            Assert.AreEqual(220f, BlockyAudio.FrequencyOf(57), 0.01f);
        }

        [Test]
        public void PlayingANote_MakesAClipOfThatLength_OnTheObjectsOwnSpeaker()
        {
            var audio = new BlockyAudio();

            var seconds = audio.PlayNote(_target, 60f, 0.5f);

            var source = _target.GetComponent<AudioSource>();
            Assert.AreEqual(0.5f, seconds, 1e-4f);
            Assert.IsNotNull(source);
            Assert.IsNotNull(source.clip);
            Assert.AreEqual(0.5f, source.clip.length, 0.01f);
            Assert.AreEqual(BlockyAudio.SampleRate, source.clip.frequency);
        }

        [Test]
        public void TheSameNoteTwice_ReusesOneClip()
        {
            var audio = new BlockyAudio();

            audio.PlayNote(_target, 60f, 0.5f);
            var first = _target.GetComponent<AudioSource>().clip;
            audio.PlayNote(_target, 60f, 0.5f);
            var second = _target.GetComponent<AudioSource>().clip;

            Assert.AreSame(first, second, "a tune plays the same few notes over and over");
        }

        [Test]
        public void TheToneStartsAndEndsQuietly_SoItDoesNotClick()
        {
            var audio = new BlockyAudio();
            audio.PlayNote(_target, 60f, 0.2f);
            var clip = _target.GetComponent<AudioSource>().clip;

            var data = new float[clip.samples];
            clip.GetData(data, 0);

            Assert.AreEqual(0f, data[0], 0.02f, "fades in");
            Assert.AreEqual(0f, data[data.Length - 1], 0.02f, "and out");
            var loudest = 0f;
            foreach (var sample in data) loudest = Mathf.Max(loudest, Mathf.Abs(sample));
            Assert.Greater(loudest, 0.2f, "but is a real sound in between");
        }

        [Test]
        public void OneSpeakerPerObject_HoweverManySoundsItMakes()
        {
            var audio = new BlockyAudio();

            audio.PlayNote(_target, 60f, 0.1f);
            audio.SetVolume(_target, 50f);
            audio.PlayNote(_target, 62f, 0.1f);

            Assert.AreEqual(1, _target.GetComponents<AudioSource>().Length);
            Assert.AreEqual(0.5f, _target.GetComponent<AudioSource>().volume, 1e-4f);
        }

        [Test]
        public void VolumeIsAPercentage_AndIsClamped()
        {
            var audio = new BlockyAudio();

            audio.SetVolume(_target, 250f);
            Assert.AreEqual(1f, _target.GetComponent<AudioSource>().volume, 1e-4f);

            audio.SetVolume(_target, -10f);
            Assert.AreEqual(0f, _target.GetComponent<AudioSource>().volume, 1e-4f);
        }

        [Test]
        public void AClipNameThatMatchesNothing_IsSilence_NotAnError()
        {
            var audio = new BlockyAudio();

            Assert.IsFalse(audio.PlayClip(_target, "no-such-sound"));
            Assert.IsFalse(audio.PlayClip(_target, string.Empty));
        }

        // ---- the blocks ------------------------------------------------------------------------------

        private static BlockDefinition Def(string blockType, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = BlockShape.Statement;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static ParamSpec Num(string key, float min, float max) => new() { key = key, kind = ParamKind.Number, min = min, max = max };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Trigger(),
            Def("motion.move_forward", new[] { Num("distance", -1000, 1000), Num("duration", 0, 1000) }),
            Def("sound.play_note", new[] { Num("note", 0, 127), Num("beats", 0, 10) }),
            Def("sound.set_volume", new[] { Num("percent", 0, 100) })
        });

        private static BlockDefinition Trigger()
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = "event.when_play_clicked";
            def.shape = BlockShape.Trigger;
            def.parameters = new ParamSpec[0];
            return def;
        }

        private static BlockNode Node(string blockType, params (string key, float value)[] parameters)
        {
            var ps = new BlockParam[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                ps[i] = new BlockParam { key = parameters[i].key, kind = ParamKind.Number, number = parameters[i].value };
            return new BlockNode { id = blockType + System.Guid.NewGuid().ToString("N")[..4], blockType = blockType, parameters = ps };
        }

        [Test]
        public void PlayNote_HoldsTheScriptForTheBeats_ThenCarriesOn()
        {
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
                            Node("sound.play_note", ("note", 60f), ("beats", 1f)),
                            Node("motion.move_forward", ("distance", 1f), ("duration", 0f))
                        }
                    }
                }
            };
            var result = ProgramCompiler.Link(program, registry);
            CollectionAssert.IsEmpty(result.Diagnostics);

            var assembly = typeof(OpTableBuilder).Assembly;
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[0]);

            scheduler.Tick(0.5f);
            Assert.AreEqual(0f, _target.transform.position.z, 1e-4f, "the note is still sounding");
            Assert.IsNotNull(_target.GetComponent<AudioSource>().clip);

            scheduler.Tick(1f); // past the beat

            Assert.AreEqual(1f, _target.transform.position.z, 1e-4f, "the block after the note ran");
        }

        [Test]
        public void StopAll_SilencesEverySpeakerBlockyStarted()
        {
            var audio = new BlockyAudio();
            audio.PlayNote(_target, 60f, 1f);

            audio.StopAll();

            Assert.IsFalse(_target.GetComponent<AudioSource>().isPlaying);
        }
    }
}
