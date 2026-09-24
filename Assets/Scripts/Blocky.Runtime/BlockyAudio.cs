using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Sound, without needing a single audio file. This project has no clips in it, so a "play [sound]" block on
    /// its own would do nothing a learner could hear — the notes are **generated**: a sine wave built into an
    /// <see cref="AudioClip"/> at the right pitch and length. A program can beep, play a tune, or mark a collision
    /// out of the box, and a clip dropped into <c>Resources/Sounds/</c> still plays by name when someone adds one.
    /// </summary>
    public sealed class BlockyAudio
    {
        public const int SampleRate = 44100;
        private const int MaxCachedTones = 64;

        /// <summary>Where <c>play sound [name]</c> looks. Nothing is there until someone adds it, and a missing clip is simply silence.</summary>
        public const string ClipFolder = "Sounds";

        private readonly Dictionary<(int note, int samples), AudioClip> _tones = new();
        private readonly List<AudioSource> _sources = new();

        /// <summary>
        /// MIDI note to hertz, the same numbering Scratch uses: 60 is middle C and 69 is A above it, at 440 Hz.
        /// </summary>
        public static float FrequencyOf(float note) => 440f * Mathf.Pow(2f, (note - 69f) / 12f);

        /// <summary>
        /// The speaker for one object, added the first time it makes a sound. 2D, not positional: a learner whose
        /// first beep is inaudible because the object is behind the camera learns the wrong lesson about their code.
        /// </summary>
        public AudioSource SourceFor(GameObject target)
        {
            if (target == null) return null;

            var source = target.GetComponent<AudioSource>();
            if (source == null)
            {
                source = target.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }

            if (!_sources.Contains(source)) _sources.Add(source);
            return source;
        }

        /// <summary>Plays <paramref name="note"/> for <paramref name="seconds"/> on <paramref name="target"/>, and answers how long it will last.</summary>
        public float PlayNote(GameObject target, float note, float seconds)
        {
            var source = SourceFor(target);
            if (source == null) return 0f;

            seconds = Mathf.Clamp(seconds, 0.02f, 10f);
            var clip = Tone(Mathf.RoundToInt(note), seconds);
            source.Stop();
            source.clip = clip;
            source.Play();
            return seconds;
        }

        /// <summary>Plays a clip from <c>Resources/Sounds</c> by name. A name nothing matches is silence, not an error.</summary>
        public bool PlayClip(GameObject target, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            var clip = Resources.Load<AudioClip>($"{ClipFolder}/{name.Trim()}");
            if (clip == null) return false;

            var source = SourceFor(target);
            if (source == null) return false;

            source.PlayOneShot(clip); // one-shot, so several sounds can overlap the way Scratch's "start sound" does
            return true;
        }

        /// <summary>0–100, as a learner types it.</summary>
        public void SetVolume(GameObject target, float percent)
        {
            var source = SourceFor(target);
            if (source != null) source.volume = Mathf.Clamp01(percent / 100f);
        }

        /// <summary>Stops every sound Blocky started — <c>stop all sounds</c>, and what Stop does to the scene.</summary>
        public void StopAll()
        {
            for (var i = _sources.Count - 1; i >= 0; i--)
            {
                if (_sources[i] == null) _sources.RemoveAt(i);
                else _sources[i].Stop();
            }
        }

        /// <summary>Forgets the generated clips and the speakers it knows about — a new play session starts from nothing.</summary>
        public void Clear()
        {
            _tones.Clear();
            _sources.Clear();
        }

        /// <summary>
        /// A sine wave at the note's pitch, with a short fade in and out so the start and end are not audible clicks.
        /// Cached, because a tune plays the same few notes over and over and each clip is real memory.
        /// </summary>
        private AudioClip Tone(int note, float seconds)
        {
            var samples = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var key = (note, samples);
            if (_tones.TryGetValue(key, out var cached) && cached != null) return cached;

            var data = new float[samples];
            var step = FrequencyOf(note) * 2f * Mathf.PI / SampleRate;
            var fade = Mathf.Min(samples / 4, Mathf.RoundToInt(0.01f * SampleRate));

            for (var i = 0; i < samples; i++)
            {
                var envelope = 1f;
                if (i < fade) envelope = i / (float)fade;
                else if (i > samples - fade) envelope = (samples - i) / (float)fade;
                data[i] = Mathf.Sin(step * i) * 0.3f * envelope;
            }

            var clip = AudioClip.Create($"BlockyNote{note}_{samples}", samples, 1, SampleRate, false);
            clip.SetData(data, 0);

            if (_tones.Count >= MaxCachedTones) _tones.Clear(); // a tune uses a handful; this only bites on nonsense input
            _tones[key] = clip;
            return clip;
        }
    }
}
