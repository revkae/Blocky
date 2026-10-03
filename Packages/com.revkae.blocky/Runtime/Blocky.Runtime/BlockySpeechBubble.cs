using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime
{
    /// <summary>
    /// The bubble an object speaks or thinks in — Scratch's <c>say</c> and <c>think</c>, and the block a learner
    /// reaches for first to find out what their program is actually doing.
    ///
    /// Built from a <see cref="TextMesh"/> and a quad behind it rather than TextMeshPro, because TMP needs its
    /// font assets imported into the project and this project has none: a TMP bubble would render nothing at all.
    /// The legacy font is always there. Both are drawn with Blocky's own two small shaders
    /// (<c>Resources/Blocky</c>), which every render pipeline draws the same way (ADR-044).
    ///
    /// The bubble hangs above everything the object shows and lies flat on the screen, so it reads from anywhere.
    /// It is the same size whatever the object's scale is: it follows the object as its child, but an elephant
    /// modelled at 0.03 and a car at 4 both get a bubble a learner can read.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockySpeechBubble : MonoBehaviour
    {
        /// <summary>The longest line of a bubble, in characters; a longer message wraps at its spaces.</summary>
        public const int MaxLineLength = 28;

        private const float HeightAboveObject = 0.35f;
        private const float HeightWithoutRenderers = 0.5f; // an object with nothing to see: just above its pivot
        private const float CharacterSize = 0.08f;
        private const int FontSize = 64;
        private const float Padding = 0.12f;
        private const float BackBehindText = 0.02f; // far enough apart for the depth buffer to tell them apart from across a scene
        private const float SmallestScale = 1e-5f;  // below this an axis counts as flat, and is left alone
        private const int BubbleSortingOrder = 1000; // above the sprites of a 2D stage, and every card before any text

        private const string BackShaderPath = "Blocky/BubbleBack";
        private const string TextShaderPath = "Blocky/BubbleText";
        private const string BuiltinFont = "LegacyRuntime.ttf";

        // Domain reload is off, so these outlive a Play session unless they are cleared — see ResetForNewPlaySession.
        private static readonly List<BlockySpeechBubble> Live = new();
        private static Material _backMaterial; // one for every bubble: they are all the same white card
        private static Material _textMaterial; // and the same font; each text's color is its own (vertex color)

        private readonly List<Renderer> _look = new(); // what the object shows, found again each time it speaks
        private TextMesh _text;
        private Renderer _textRenderer;
        private Transform _holder;
        private Transform _background;

        /// <summary>
        /// Past this distance from the main camera a bubble grows with the distance, so it stays as large on screen
        /// as it is from here — for a game whose objects are looked at from far away (a world to walk around in, a
        /// headset). Infinity, the default, keeps every bubble its real size. Reset at the start of every Play
        /// session, like Blocky's other switches: set it as the game starts.
        /// </summary>
        public static float ReadableDistance { get; set; } = float.PositiveInfinity;

        /// <summary>Test seam: the camera bubbles face and measure from, instead of the scene's main camera.</summary>
        internal static Camera CameraForTests { get; set; }

        private static Camera ViewCamera => CameraForTests != null ? CameraForTests : Camera.main;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession()
        {
            Live.Clear();
            ReadableDistance = float.PositiveInfinity;
            CameraForTests = null;
        }

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

        /// <summary>
        /// <paramref name="message"/> with line breaks put in at its spaces, so no line is longer than
        /// <paramref name="maxLineLength"/> — a sentence becomes a bubble, not a banner across the scene. A single
        /// word longer than a line is left whole, and line breaks already in the message are kept.
        /// </summary>
        public static string Wrap(string message, int maxLineLength = MaxLineLength)
        {
            if (string.IsNullOrEmpty(message) || maxLineLength <= 0 || message.Length <= maxLineLength) return message;

            var wrapped = new StringBuilder(message.Length + 8);
            foreach (var paragraph in message.Split('\n'))
            {
                if (wrapped.Length > 0) wrapped.Append('\n');
                var lineLength = 0;

                foreach (var word in paragraph.Split(' '))
                {
                    if (lineLength > 0 && lineLength + 1 + word.Length > maxLineLength)
                    {
                        wrapped.Append('\n');
                        lineLength = 0;
                    }
                    else if (lineLength > 0)
                    {
                        wrapped.Append(' ');
                        lineLength++;
                    }

                    wrapped.Append(word);
                    lineLength += word.Length;
                }
            }
            return wrapped.ToString();
        }

        // No Awake: a component added outside play mode never gets one, and the tests add bubbles that way. The
        // bubble builds and registers itself on first use instead, which happens identically in both modes.
        private void OnDestroy() => Live.Remove(this);

        private void SetMessage(string message, bool think)
        {
            if (_text == null) Build();

            _text.text = Wrap(message);
            _text.fontStyle = think ? FontStyle.Italic : FontStyle.Normal;
            _text.gameObject.SetActive(true);
            if (_background != null) _background.gameObject.SetActive(true);

            // The object may have gained or lost parts since it last spoke (a clone, a model swapped in).
            BlockyMaterials.RenderersUnder(gameObject, _look);
            _look.RemoveAll(IsEffect);
            Place(ViewCamera); // now, not a frame from now: the bubble never flashes where it used to be
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

            var holder = new GameObject("BlockyBubble");
            holder.transform.SetParent(transform, false);
            _holder = holder.transform;

            // The card goes in first and slightly behind, so the text is never hidden by it.
            var backMaterial = BackMaterial();
            if (backMaterial != null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Back";
                // A bubble is not something to bump into. Destroy is right while the game runs, but outside play
                // mode it only logs an error — and the tests live there.
                var collider = quad.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
                quad.transform.SetParent(holder.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, BackBehindText);
                var quadRenderer = quad.GetComponent<MeshRenderer>();
                quadRenderer.sharedMaterial = backMaterial;
                quadRenderer.sortingOrder = BubbleSortingOrder; // on a 2D stage, drawn over the sprites rather than among them
                quad.AddComponent<BlockyBubblePart>(); // not part of the object's look: "change color" and Reset leave it alone
                _background = quad.transform;
            }

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(holder.transform, false);
            textObject.AddComponent<BlockyBubblePart>();
            _text = textObject.AddComponent<TextMesh>();
            _text.characterSize = CharacterSize;
            _text.fontSize = FontSize;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = Color.black;
            _textRenderer = textObject.GetComponent<MeshRenderer>();
            if (_textRenderer != null)
            {
                _textRenderer.sortingOrder = BubbleSortingOrder + 1;
                var textMaterial = TextMaterial(_text);
                if (textMaterial != null) _textRenderer.sharedMaterial = textMaterial;
            }

            Clear();
        }

        /// <summary>
        /// The white card. Blocky's own shader rather than one of a render pipeline's: asking only whether URP's
        /// unlit shader exists is not enough — a project can have the URP package installed and still render with
        /// the built-in pipeline, where URP's shaders draw as the magenta error color.
        /// </summary>
        private static Material BackMaterial()
        {
            if (_backMaterial != null) return _backMaterial;

            var shader = Resources.Load<Shader>(BackShaderPath);
            if (shader == null) shader = Shader.Find("Sprites/Default"); // the package's Resources are gone: any unlit shader
            if (shader == null) return null;

            _backMaterial = new Material(shader) { name = "Blocky Bubble Back", color = Color.white, hideFlags = HideFlags.HideAndDontSave };
            return _backMaterial;
        }

        /// <summary>
        /// The font, drawn with a shader that is hidden by what is in front of it. Null — the font's own material,
        /// which draws over everything — when the shader or the built-in font can't be found.
        /// </summary>
        private static Material TextMaterial(TextMesh text)
        {
            if (_textMaterial != null)
            {
                text.font = Resources.GetBuiltinResource<Font>(BuiltinFont);
                return _textMaterial;
            }

            var shader = Resources.Load<Shader>(TextShaderPath);
            var font = Resources.GetBuiltinResource<Font>(BuiltinFont);
            if (shader == null || font == null || font.material == null) return null;

            text.font = font;
            _textMaterial = new Material(shader)
            {
                name = "Blocky Bubble Text",
                mainTexture = font.material.mainTexture,
                hideFlags = HideFlags.HideAndDontSave
            };
            return _textMaterial;
        }

        // LateUpdate, not Update: the camera and the object have finished moving by then, so the bubble never sits
        // or faces where they were a frame ago.
        private void LateUpdate()
        {
            if (_text == null || !_text.gameObject.activeSelf) return;
            Place(ViewCamera);
        }

        /// <summary>
        /// Puts the bubble over the object, the right size and flat on <paramref name="camera"/>'s screen (null: no
        /// camera to face or measure from — it keeps its size and the way it was turned).
        /// </summary>
        private void Place(Camera camera)
        {
            var top = TopOfObject();
            var size = 1f;

            if (camera != null)
            {
                var cameraTransform = camera.transform;
                if (ReadableDistance > 0f && !float.IsInfinity(ReadableDistance))
                    size = Mathf.Max(1f, Vector3.Distance(cameraTransform.position, top) / ReadableDistance);

                // Turned the way the camera is, not towards where the camera stands: a bubble that faces the camera's
                // position is seen at an angle everywhere but the middle of the screen, and its text leans.
                _holder.rotation = cameraTransform.rotation;
            }

            // The object's own scale is taken back out, so the bubble is `size` in the world whatever it hangs under.
            var parentScale = transform.lossyScale;
            _holder.localScale = new Vector3(Unscaled(size, parentScale.x), Unscaled(size, parentScale.y), Unscaled(size, parentScale.z));

            var textSize = _textRenderer != null ? (Vector2)_textRenderer.localBounds.size : Vector2.zero;
            if (_background != null) _background.localScale = new Vector3(textSize.x + Padding, textSize.y + Padding, 1f);

            // The text is anchored at its middle: lift it by half its height, so its bottom edge clears the object.
            _holder.position = top + Vector3.up * ((HeightAboveObject + (textSize.y + Padding) * 0.5f) * size);
        }

        /// <summary>
        /// Smoke, sparks and trails reach far past the object they belong to; a bubble measured from them would hang
        /// in the air above nothing. (The particle renderer is named rather than typed, so Blocky needs no particle module.)
        /// </summary>
        private static readonly System.Predicate<Renderer> IsEffect = part =>
            part is TrailRenderer || part is LineRenderer || part.GetType().Name == "ParticleSystemRenderer";

        private static float Unscaled(float size, float parentScale) =>
            Mathf.Abs(parentScale) > SmallestScale ? size / parentScale : size;

        /// <summary>
        /// The middle of the top of everything the object shows, in the world. An imported model keeps its meshes
        /// under a root with no renderer and a pivot that may be at its feet, its middle or off to one side, so the
        /// root's own renderer and pivot say little about where "above it" is.
        /// </summary>
        private Vector3 TopOfObject()
        {
            var found = false;
            Bounds bounds = default;
            foreach (var part in _look)
            {
                if (part == null) continue; // destroyed since the object last spoke
                if (found) bounds.Encapsulate(part.bounds);
                else
                {
                    bounds = part.bounds;
                    found = true;
                }
            }

            return found
                ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
                : transform.position + Vector3.up * HeightWithoutRenderers;
        }
    }
}
