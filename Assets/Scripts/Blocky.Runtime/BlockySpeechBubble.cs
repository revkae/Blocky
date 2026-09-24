using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// The bubble an object speaks or thinks in — Scratch's <c>say</c> and <c>think</c>, and the block a learner
    /// reaches for first to find out what their program is actually doing.
    ///
    /// Built from a <see cref="TextMesh"/> and a quad behind it rather than TextMeshPro, because TMP needs its
    /// font assets imported into the project and this project has none: a TMP bubble would render nothing at all.
    /// The legacy font is always there, and it was checked in Play mode under URP before this was written.
    /// The bubble hangs above the object and turns to face the camera every frame, so it reads from anywhere.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockySpeechBubble : MonoBehaviour
    {
        private const float HeightAboveObject = 0.35f;
        private const float CharacterSize = 0.08f;
        private const int FontSize = 64;
        private const float Padding = 0.12f;

        // Domain reload is off, so this outlives a Play session unless it is cleared — see ResetForNewPlaySession.
        private static readonly List<BlockySpeechBubble> Live = new();

        private TextMesh _text;
        private Transform _background;
        private float _topOfObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession() => Live.Clear();

        /// <summary>Shows <paramref name="message"/> over <paramref name="target"/>. An empty message hides the bubble, as in Scratch.</summary>
        public static void Show(GameObject target, string message, bool think)
        {
            if (target == null) return;

            if (string.IsNullOrEmpty(message))
            {
                Hide(target);
                return;
            }

            var bubble = target.GetComponent<BlockySpeechBubble>();
            if (bubble == null) bubble = target.AddComponent<BlockySpeechBubble>();
            bubble.SetMessage(message, think);
        }

        /// <summary>Clears the bubble over <paramref name="target"/>, if it has one.</summary>
        public static void Hide(GameObject target)
        {
            if (target == null) return;
            var bubble = target.GetComponent<BlockySpeechBubble>();
            if (bubble != null) bubble.Clear();
        }

        /// <summary>Clears every bubble in the scene — what Stop does, so a stopped run leaves nothing on screen.</summary>
        public static void HideAll()
        {
            for (var i = Live.Count - 1; i >= 0; i--)
            {
                if (Live[i] == null) Live.RemoveAt(i);
                else Live[i].Clear();
            }
        }

        // No Awake: a component added outside play mode never gets one, and the tests add bubbles that way. The
        // bubble builds and registers itself on first use instead, which happens identically in both modes.
        private void OnDestroy() => Live.Remove(this);

        private void SetMessage(string message, bool think)
        {
            if (_text == null) Build();

            _text.text = message;
            _text.fontStyle = think ? FontStyle.Italic : FontStyle.Normal;
            _text.gameObject.SetActive(true);
            if (_background != null) _background.gameObject.SetActive(true);
            FitBackground();
        }

        private void Clear()
        {
            if (_text == null) return;
            _text.text = string.Empty;
            _text.gameObject.SetActive(false);
            if (_background != null) _background.gameObject.SetActive(false);
        }

        private void Build()
        {
            if (!Live.Contains(this)) Live.Add(this);
            _topOfObject = TopOfObject();

            var holder = new GameObject("BlockyBubble");
            holder.transform.SetParent(transform, false);

            // The quad goes in first and slightly behind, so the text is never hidden by it.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Back";
                // A bubble is not something to bump into. Destroy is right while the game runs, but outside play
                // mode it only logs an error — and the tests live there.
                var collider = quad.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
                quad.transform.SetParent(holder.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                var material = new Material(shader) { color = new Color(1f, 1f, 1f, 0.92f) };
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                _background = quad.transform;
            }

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(holder.transform, false);
            _text = textObject.AddComponent<TextMesh>();
            _text.characterSize = CharacterSize;
            _text.fontSize = FontSize;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = Color.black;

            holder.transform.localPosition = new Vector3(0f, _topOfObject + HeightAboveObject, 0f);
            Clear();
        }

        /// <summary>Half the object's height, so the bubble clears whatever shape it is sitting on.</summary>
        private float TopOfObject()
        {
            var renderer = GetComponent<Renderer>();
            return renderer != null ? renderer.bounds.extents.y : 0.5f;
        }

        private void FitBackground()
        {
            if (_background == null || _text == null) return;
            var bounds = _text.GetComponent<Renderer>().bounds.size;
            _background.localScale = new Vector3(bounds.x + Padding, bounds.y + Padding, 1f);
        }

        // LateUpdate, not Update: the camera has finished moving by then, so the bubble never faces where the
        // camera was a frame ago.
        private void LateUpdate()
        {
            if (_text == null || !_text.gameObject.activeSelf) return;

            var camera = Camera.main;
            if (camera == null) return;

            // The camera's up, not the world's: with world up the bubble leans whenever the camera looks down at
            // it. Using the camera's own up makes the text sit flat on screen from any angle.
            var holder = _text.transform.parent;
            holder.rotation = Quaternion.LookRotation(holder.position - camera.transform.position, camera.transform.up);
        }
    }
}
