using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Keeps track of every programmed object — how it started, for Reset, and how it is at any moment
    /// (<see cref="SaveMoment"/>), for Step back. Records exactly what blocks can change: position, rotation,
    /// scale, and — for every renderer under it — visibility, materials and colors, and a physics body's motion. A new
    /// block that changes something else (a light, a sound) must be added here too, or Reset and Step back won't
    /// undo it.
    /// An object's start is where it was when first captured, until <see cref="Recapture"/> moves it — what a game
    /// that lets its objects be placed between runs calls when one has been (ADR-043).
    /// </summary>
    public sealed class WorldSnapshot
    {
        internal sealed class Tracked
        {
            public GameObject GameObject;
            public Renderer[] Renderers;  // every renderer under it, its own included: "change color" and "set visible" reach them all
            public Rigidbody Body;
#if BLOCKY_PHYSICS2D // set by Blocky.Runtime.asmdef when the project has Unity's 2D physics module
            public Rigidbody2D Body2D;
#endif
            public ObjectState Start;

            /// <summary>
            /// The materials the object's renderers had when each was first seen. These are never recolored: in the
            /// Editor they are the project's own assets, and anywhere they may be shared with other objects. Only the
            /// per-object copies "change color" makes are (ADR-008).
            /// </summary>
            public readonly HashSet<Material> Originals = new();
        }

        internal struct ObjectState
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public bool Active;
            public RendererLook[] Looks; // one per renderer under the object, as it was then
            public Vector3 Velocity;
            public Vector3 AngularVelocity;
            public Vector2 Velocity2D;
            public float AngularVelocity2D;
        }

        /// <summary>One renderer's visibility, materials and colors at one moment. It names its renderer, so it stays right when the object's renderers change.</summary>
        internal struct RendererLook
        {
            public Renderer Renderer;
            public bool Enabled;         // "set visible" switches every renderer under the object
            public Material[] Materials; // shared: reading .materials would make copies
            public Color[] Colors;       // per material; meaningful where HasColor says so
            public bool[] HasColor;
            public Color SpriteColor;    // a sprite is tinted through its own color, not its material (ChangeColorOp)
        }

        private readonly Dictionary<GameObject, Tracked> _tracked = new();
        private readonly Dictionary<GameObject, List<GameObject>> _driven = new(); // objects another object's scripts act on ("as [object]")
        private readonly List<GameObject> _destroyed = new();
        private readonly List<Renderer> _renderers = new(); // reused by every scan
        private readonly List<Material> _materials = new(); // reused by every read and comparison

        /// <summary>How many objects are tracked.</summary>
        public int Count => _tracked.Count;

        /// <summary>Starts tracking <paramref name="go"/> and remembers it as it is now — the first time only, so later calls keep its true starting state.</summary>
        public void Capture(GameObject go)
        {
            if (go == null || _tracked.ContainsKey(go)) return;

            var tracked = new Tracked { GameObject = go };
            FindParts(tracked);
            tracked.Start = StartState(tracked);
            _tracked.Add(go, tracked);
        }

        /// <summary>
        /// Starts tracking <paramref name="driven"/> as <see cref="Capture"/> does, and remembers that
        /// <paramref name="driver"/>'s scripts act on it ("as [object]") — so resetting or stepping back the driver
        /// alone covers it too.
        /// </summary>
        public void CaptureDriven(GameObject driver, GameObject driven)
        {
            Capture(driven);
            if (driver == null || driven == null || driver == driven) return;

            if (!_driven.TryGetValue(driver, out var objects)) _driven[driver] = objects = new List<GameObject>();
            if (!objects.Contains(driven)) objects.Add(driven);
        }

        /// <summary>
        /// Makes <paramref name="go"/>'s current state its start — what Reset returns it to — and starts tracking it
        /// if it wasn't. For a game where objects are placed between runs: call it when one has been moved, recolored
        /// or given new parts. Its original materials stay the ones it was first seen with, so Reset still never
        /// recolors them.
        /// </summary>
        public void Recapture(GameObject go)
        {
            if (go == null) return;
            if (!_tracked.TryGetValue(go, out var tracked))
            {
                Capture(go);
                return;
            }

            FindParts(tracked); // renderers and bodies may have come or gone since
            tracked.Start = StartState(tracked);
        }

        /// <summary>Makes every tracked object's current state its start, and forgets any that were destroyed.</summary>
        public void RecaptureAll()
        {
            _destroyed.Clear();
            foreach (var (go, tracked) in _tracked)
            {
                if (go == null)
                {
                    _destroyed.Add(go);
                    continue;
                }

                FindParts(tracked);
                tracked.Start = StartState(tracked);
            }

            foreach (var go in _destroyed) Forget(go);
        }

        /// <summary>Stops tracking <paramref name="go"/> — the game deleted it, or it is no longer programmable. Reset leaves it alone from then on.</summary>
        public void Forget(GameObject go)
        {
            if (ReferenceEquals(go, null)) return; // a destroyed object still names its entry

            _tracked.Remove(go);
            _driven.Remove(go);
            foreach (var objects in _driven.Values) objects.Remove(go);
        }

        /// <summary>Puts every tracked object back as it started, and forgets any that were destroyed.</summary>
        public void RestoreAll()
        {
            _destroyed.Clear();
            foreach (var (go, tracked) in _tracked)
            {
                if (go == null) _destroyed.Add(go);
                else Apply(tracked, tracked.Start);
            }

            foreach (var go in _destroyed) Forget(go);
        }

        /// <summary>
        /// Puts one tracked object back as it started — the run bar's Reset for the open object — along with the
        /// objects its scripts acted on (<see cref="CaptureDriven"/>). Does nothing for an object that isn't tracked.
        /// </summary>
        public void RestoreObject(GameObject go)
        {
            if (go == null) return;
            if (_tracked.TryGetValue(go, out var tracked)) Apply(tracked, tracked.Start);

            if (!_driven.TryGetValue(go, out var objects)) return;
            foreach (var driven in objects)
                if (driven != null && _tracked.TryGetValue(driven, out var other)) Apply(other, other.Start);
        }

        /// <summary>
        /// Records how every tracked object is right now, for <see cref="RestoreMoment"/> — or, with
        /// <paramref name="only"/>, just that object and the objects its scripts act on.
        /// </summary>
        public WorldMoment SaveMoment(GameObject only = null)
        {
            var objects = new List<Tracked>(only == null ? _tracked.Count : 1);
            if (only == null)
            {
                foreach (var (go, tracked) in _tracked)
                    if (go != null) objects.Add(tracked);
            }
            else
            {
                if (_tracked.TryGetValue(only, out var tracked)) objects.Add(tracked);
                if (_driven.TryGetValue(only, out var drivenObjects))
                    foreach (var driven in drivenObjects)
                        if (driven != null && _tracked.TryGetValue(driven, out var other)) objects.Add(other);
            }

            var states = new ObjectState[objects.Count];
            for (var i = 0; i < objects.Count; i++) states[i] = Read(objects[i]);
            return new WorldMoment(objects.ToArray(), states);
        }

        /// <summary>Puts every object recorded in <paramref name="moment"/> back as it was then. Objects destroyed since are skipped.</summary>
        public void RestoreMoment(WorldMoment moment)
        {
            for (var i = 0; i < moment.Objects.Length; i++)
                if (moment.Objects[i].GameObject != null) Apply(moment.Objects[i], moment.States[i]);
        }

        /// <summary>
        /// Finds the parts of the object that blocks can change. A renderer seen for the first time adds its materials
        /// to the originals; one already known keeps the originals it had, whatever it wears now.
        /// </summary>
        private void FindParts(Tracked tracked)
        {
            var go = tracked.GameObject;
            tracked.Body = go.GetComponent<Rigidbody>();
#if BLOCKY_PHYSICS2D
            tracked.Body2D = go.GetComponent<Rigidbody2D>();
#endif

            var known = tracked.Renderers;
            BlockyMaterials.RenderersUnder(go, _renderers);
            foreach (var renderer in _renderers)
            {
                if (known != null && Array.IndexOf(known, renderer) >= 0) continue;

                renderer.GetSharedMaterials(_materials);
                foreach (var material in _materials)
                    if (material != null) tracked.Originals.Add(material);
            }

            tracked.Renderers = _renderers.ToArray();
            _renderers.Clear();
            _materials.Clear();
        }

        /// <summary>The object as it is now, standing still: Reset leaves everything at rest.</summary>
        private ObjectState StartState(Tracked tracked)
        {
            var state = Read(tracked);
            state.Velocity = Vector3.zero;
            state.AngularVelocity = Vector3.zero;
            state.Velocity2D = Vector2.zero;
            state.AngularVelocity2D = 0f;
            return state;
        }

        private ObjectState Read(Tracked tracked)
        {
            var go = tracked.GameObject;
            var transform = go.transform;
            var state = new ObjectState
            {
                LocalPosition = transform.localPosition,
                LocalRotation = transform.localRotation,
                LocalScale = transform.localScale,
                Active = go.activeSelf,
                Looks = new RendererLook[tracked.Renderers.Length]
            };

            for (var i = 0; i < tracked.Renderers.Length; i++)
                state.Looks[i] = ReadLook(tracked.Renderers[i]);

            var body = tracked.Body;
            if (body != null && !body.isKinematic)
            {
                state.Velocity = body.linearVelocity;
                state.AngularVelocity = body.angularVelocity;
            }

#if BLOCKY_PHYSICS2D
            var body2D = tracked.Body2D;
            if (body2D != null && body2D.bodyType == RigidbodyType2D.Dynamic)
            {
                state.Velocity2D = body2D.linearVelocity;
                state.AngularVelocity2D = body2D.angularVelocity;
            }
#endif

            return state;
        }

        private RendererLook ReadLook(Renderer renderer)
        {
            if (renderer == null) return default; // destroyed since the object was scanned: nothing to put back

            renderer.GetSharedMaterials(_materials);
            var look = new RendererLook
            {
                Renderer = renderer,
                Enabled = renderer.enabled,
                Materials = _materials.ToArray(),
                Colors = new Color[_materials.Count],
                HasColor = new bool[_materials.Count]
            };
            for (var i = 0; i < _materials.Count; i++)
                look.HasColor[i] = BlockyMaterials.TryGetColor(_materials[i], out look.Colors[i]);
            if (renderer is SpriteRenderer sprite) look.SpriteColor = sprite.color;

            _materials.Clear();
            return look;
        }

        private void Apply(Tracked tracked, in ObjectState state)
        {
            var go = tracked.GameObject;
            var transform = go.transform;
            transform.localPosition = state.LocalPosition;
            transform.localRotation = state.LocalRotation;
            transform.localScale = state.LocalScale;
            if (go.activeSelf != state.Active) go.SetActive(state.Active);

            foreach (var look in state.Looks) ApplyLook(tracked, look);

            var body = tracked.Body;
            if (body != null)
            {
                body.position = transform.position;
                body.rotation = transform.rotation;
                if (!body.isKinematic) // a kinematic body has no velocity to set (Unity warns if you try)
                {
                    body.linearVelocity = state.Velocity;
                    body.angularVelocity = state.AngularVelocity;
                }
            }

#if BLOCKY_PHYSICS2D
            var body2D = tracked.Body2D;
            if (body2D != null)
            {
                body2D.position = transform.position;
                body2D.rotation = transform.eulerAngles.z;
                if (body2D.bodyType == RigidbodyType2D.Dynamic)
                {
                    body2D.linearVelocity = state.Velocity2D;
                    body2D.angularVelocity = state.AngularVelocity2D;
                }
            }
#endif
        }

        private void ApplyLook(Tracked tracked, in RendererLook look)
        {
            var renderer = look.Renderer;
            if (renderer == null) return;

            renderer.enabled = look.Enabled;

            // "change color" works on per-object copies of the materials (ADR-008). Pointing back at the materials of
            // that moment undoes the swap; the copies themselves are freed by Unity's next unused-asset sweep.
            if (!HasMaterials(renderer, look.Materials)) renderer.sharedMaterials = look.Materials;

            // Only a copy is ever recolored — never a material the object started with, which in the Editor is the project asset itself.
            for (var i = 0; i < look.Materials.Length; i++)
            {
                var material = look.Materials[i];
                if (look.HasColor[i] && material != null && !tracked.Originals.Contains(material))
                    BlockyMaterials.SetColor(material, look.Colors[i]);
            }

            if (renderer is SpriteRenderer sprite) sprite.color = look.SpriteColor;
        }

        private bool HasMaterials(Renderer renderer, Material[] wanted)
        {
            renderer.GetSharedMaterials(_materials);
            var same = _materials.Count == wanted.Length;
            for (var i = 0; same && i < wanted.Length; i++) same = _materials[i] == wanted[i];
            _materials.Clear();
            return same;
        }
    }

    /// <summary>How every tracked object was at one point in time, from <see cref="WorldSnapshot.SaveMoment"/>.</summary>
    public sealed class WorldMoment
    {
        internal readonly WorldSnapshot.Tracked[] Objects;
        internal readonly WorldSnapshot.ObjectState[] States;

        internal WorldMoment(WorldSnapshot.Tracked[] objects, WorldSnapshot.ObjectState[] states)
        {
            Objects = objects;
            States = states;
        }
    }
}
