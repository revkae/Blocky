using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor
{
    /// <summary>
    /// Scratch-style block silhouette, drawn with <see cref="Painter2D"/>. The shape is the documentation of what
    /// connects where: a notch on top means something can sit above; a tab underneath means something can
    /// attach below; a hat has no notch (nothing goes above an event); a cap has no tab (nothing runs after
    /// <c>forever</c>); each C-mouth has its own tab and notch so blocks click inside it.
    /// The bottom tab is drawn below the element's own rect, straight into the next block's notch — which is why
    /// sequence containers stack blocks with zero margin.
    /// </summary>
    public struct BlockOutline
    {
        public const float CornerRadius = 4f;
        public const float NotchStart = 12f;
        public const float NotchSlope = 6f;
        public const float NotchFlat = 16f;
        public const float NotchDepth = 6f;
        public const float SpineWidth = 16f;
        public const float HatBumpHeight = 14f;
        public const float HatBumpWidth = 80f;

        private const float NotchEnd = NotchStart + NotchSlope * 2f + NotchFlat;

        public float Width;
        public float Height;
        public bool Hat;
        public bool BottomTab;
        public bool Hexagon; // a condition (or an empty condition slot): pointed ends, no notch or tab — it never stacks
        public bool Pill;    // a reporter (or an empty input): fully rounded ends, no notch or tab — it never stacks either
        public List<Vector2> Mouths; // (top, bottom) in local y, one per C-mouth, top to bottom

        /// <summary>Class that marks the block the user clicked; drawn as a white outline around the real silhouette.</summary>
        public const string SelectedClass = "blocky-selected";

        /// <summary>Class that marks the block a script is running right now; drawn as a thick yellow outline, like Scratch's glow.</summary>
        public const string RunningClass = "blocky-running";

        private static readonly Color RunningStroke = new(1f, 0.86f, 0.1f);

        public void Draw(Painter2D p, Color fill, bool selected = false, bool running = false)
        {
            if (float.IsNaN(Width) || float.IsNaN(Height) || Width <= 0f || Height <= 0f) return;

            // One path, one Fill, one Stroke — the only sequence verified to render. Re-tracing the path for
            // extra strokes produced no visible geometry, so "selected" and "running" change colors, not the number
            // of passes. Running wins the outline (it's the live signal); selected still lightens the fill.
            p.fillColor = selected ? Color.Lerp(fill, Color.white, 0.18f) : fill;
            p.strokeColor = running ? RunningStroke : selected ? Color.white : new Color(fill.r * 0.78f, fill.g * 0.78f, fill.b * 0.78f, 1f);
            p.lineWidth = running ? 4f : selected ? 3f : 1f;
            Trace(p);
            p.Fill();
            p.Stroke();
        }

        private void Trace(Painter2D p)
        {
            var w = Width;
            var h = Height;
            const float r = CornerRadius;

            p.BeginPath();

            if (Pill)
            {
                var cap = Mathf.Min(h / 2f, w / 2f); // fully round ends, however short the block is
                p.MoveTo(new Vector2(cap, 0f));
                p.LineTo(new Vector2(w - cap, 0f));
                p.ArcTo(new Vector2(w, 0f), new Vector2(w, cap), cap);
                p.LineTo(new Vector2(w, h - cap));
                p.ArcTo(new Vector2(w, h), new Vector2(w - cap, h), cap);
                p.LineTo(new Vector2(cap, h));
                p.ArcTo(new Vector2(0f, h), new Vector2(0f, h - cap), cap);
                p.LineTo(new Vector2(0f, cap));
                p.ArcTo(new Vector2(0f, 0f), new Vector2(cap, 0f), cap);
                p.ClosePath();
                return;
            }

            if (Hexagon)
            {
                var point = Mathf.Min(h / 2f, w / 2f); // how far the pointed ends reach in
                p.MoveTo(new Vector2(0f, h / 2f));
                p.LineTo(new Vector2(point, 0f));
                p.LineTo(new Vector2(w - point, 0f));
                p.LineTo(new Vector2(w, h / 2f));
                p.LineTo(new Vector2(w - point, h));
                p.LineTo(new Vector2(point, h));
                p.ClosePath();
                return;
            }

            if (Hat)
            {
                p.MoveTo(new Vector2(0f, HatBumpHeight));
                p.BezierCurveTo(new Vector2(HatBumpWidth * 0.25f, -4f), new Vector2(HatBumpWidth * 0.75f, -4f), new Vector2(HatBumpWidth, HatBumpHeight));
                p.LineTo(new Vector2(w - r, HatBumpHeight));
                p.ArcTo(new Vector2(w, HatBumpHeight), new Vector2(w, HatBumpHeight + r), r);
            }
            else
            {
                p.MoveTo(new Vector2(0f, r));
                p.ArcTo(new Vector2(0f, 0f), new Vector2(r, 0f), r);
                Notch(p, 0f, 0f);
                p.LineTo(new Vector2(w - r, 0f));
                p.ArcTo(new Vector2(w, 0f), new Vector2(w, r), r);
            }

            if (Mouths != null)
                foreach (var mouth in Mouths)
                {
                    var top = mouth.x;
                    var bottom = mouth.y;
                    const float s = SpineWidth;

                    p.LineTo(new Vector2(w, top - r));
                    p.ArcTo(new Vector2(w, top), new Vector2(w - r, top), r);
                    Tab(p, s, top); // the arm above the mouth hangs a tab into it, for the first inner block's notch
                    p.LineTo(new Vector2(s + r, top));
                    p.ArcTo(new Vector2(s, top), new Vector2(s, top + r), r);
                    p.LineTo(new Vector2(s, bottom - r));
                    p.ArcTo(new Vector2(s, bottom), new Vector2(s + r, bottom), r);
                    Notch(p, s, bottom); // the arm below the mouth has a notch, catching the last inner block's tab
                    p.LineTo(new Vector2(w - r, bottom));
                    p.ArcTo(new Vector2(w, bottom), new Vector2(w, bottom + r), r);
                }

            p.LineTo(new Vector2(w, h - r));
            p.ArcTo(new Vector2(w, h), new Vector2(w - r, h), r);
            if (BottomTab) Tab(p, 0f, h);
            p.LineTo(new Vector2(r, h));
            p.ArcTo(new Vector2(0f, h), new Vector2(0f, h - r), r);
            p.ClosePath();
        }

        // Travelling right along an edge whose body lies below it: dip down, cutting a notch into the body.
        private static void Notch(Painter2D p, float x, float y)
        {
            p.LineTo(new Vector2(x + NotchStart, y));
            p.LineTo(new Vector2(x + NotchStart + NotchSlope, y + NotchDepth));
            p.LineTo(new Vector2(x + NotchStart + NotchSlope + NotchFlat, y + NotchDepth));
            p.LineTo(new Vector2(x + NotchEnd, y));
        }

        // Travelling left along an edge whose body lies above it: bulge down, hanging a tab below the body.
        private static void Tab(Painter2D p, float x, float y)
        {
            p.LineTo(new Vector2(x + NotchEnd, y));
            p.LineTo(new Vector2(x + NotchStart + NotchSlope + NotchFlat, y + NotchDepth));
            p.LineTo(new Vector2(x + NotchStart + NotchSlope, y + NotchDepth));
            p.LineTo(new Vector2(x + NotchStart, y));
        }
    }

    /// <summary>
    /// Draws a <see cref="BlockOutline"/> as an element's background. The fill comes from the USS custom property
    /// <c>--blocky-fill</c> (set per category in blocky-*.uss), so colors stay in stylesheets, not code.
    /// </summary>
    internal sealed class BlockShapePainter
    {
        private static readonly CustomStyleProperty<Color> FillProperty = new("--blocky-fill");
        private static readonly Color FallbackFill = new(0.55f, 0.55f, 0.58f);

        private readonly VisualElement _element;
        private readonly Func<BlockOutline> _outline;
        private readonly float _shade;
        private Color _fill = FallbackFill;

        /// <param name="shade">Multiplies the fill — below 1 draws a darker hole in the block's color (an empty condition slot).</param>
        public BlockShapePainter(VisualElement element, Func<BlockOutline> outline, float shade = 1f)
        {
            _element = element;
            _outline = outline;
            _shade = shade;
            element.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            element.RegisterCallback<GeometryChangedEvent>(_ => element.MarkDirtyRepaint());
            element.generateVisualContent += Draw;
        }

        /// <summary>Repaint when a child the outline is measured from (a C-mouth) changes size.</summary>
        public void TrackGeometryOf(VisualElement child) =>
            child.RegisterCallback<GeometryChangedEvent>(_ => _element.MarkDirtyRepaint());

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (!evt.customStyle.TryGetValue(FillProperty, out var fill)) return;
            _fill = fill;
            _element.MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var fill = new Color(_fill.r * _shade, _fill.g * _shade, _fill.b * _shade, 1f);
            _outline().Draw(mgc.painter2D, fill, _element.ClassListContains(BlockOutline.SelectedClass),
                _element.ClassListContains(BlockOutline.RunningClass));
        }
    }
}
