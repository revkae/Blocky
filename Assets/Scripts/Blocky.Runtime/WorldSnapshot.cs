using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Keeps track of every programmed object — how it started, for Reset, and how it is at any moment
    /// (<see cref="SaveMoment"/>), for Step back. Records exactly what blocks can change: position, rotation,
    /// scale, visibility, material and color, and a physics body's motion. A new block that changes something
    /// else (a light, a sound) must be added here too, or Reset and Step back won't undo it.
    /// </summary>
    public sealed class WorldSnapshot
    {
        internal sealed class Tracked
        {
            public GameObject GameObject;
            public Renderer Renderer;
            public Rigidbody Body;
#if BLOCKY_PHYSICS2D // set by Blocky.Runtime.asmdef when the project has Unity's 2D physics module
            public Rigidbody2D Body2D;
#endif
            public ObjectState Start;
        }

        internal struct ObjectState
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public bool Active;
            public bool RendererEnabled;
            public Material Material;
            public bool HasColor;
            public Color Color;
            public Color SpriteColor; // a sprite is tinted through its own color, not its material (ChangeColorOp)
            public Vector3 Velocity;
            public Vector3 AngularVelocity;
            public Vector2 Velocity2D;
            public float AngularVelocity2D;
        }

        private readonly Dictionary<GameObject, Tracked> _tracked = new();
        private readonly List<GameObject> _destroyed = new();

        /// <summary>How many objects are tracked.</summary>
        public int Count => _tracked.Count;

        /// <summary>Starts tracking <paramref name="go"/> and remembers it as it is now — the first time only, so later calls keep its true starting state.</summary>
        public void Capture(GameObject go)
        {
            if (go == null || _tracked.ContainsKey(go)) return;

            var tracked = new Tracked { GameObject = go, Renderer = go.GetComponent<Renderer>(), Body = go.GetComponent<Rigidbody>() };
#if BLOCKY_PHYSICS2D
            tracked.Body2D = go.GetComponent<Rigidbody2D>();
#endif
            tracked.Start = Read(tracked);
            tracked.Start.Velocity = Vector3.zero; // Reset leaves everything standing still
            tracked.Start.AngularVelocity = Vector3.zero;
            tracked.Start.Velocity2D = Vector2.zero;
            tracked.Start.AngularVelocity2D = 0f;
            _tracked.Add(go, tracked);
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

            foreach (var go in _destroyed) _tracked.Remove(go);
        }

        /// <summary>Records how every tracked object is right now, for <see cref="RestoreMoment"/>.</summary>
        public WorldMoment SaveMoment()
        {
            var objects = new List<Tracked>(_tracked.Count);
            foreach (var (go, tracked) in _tracked)
                if (go != null) objects.Add(tracked);

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

        private static ObjectState Read(Tracked tracked)
        {
            var go = tracked.GameObject;
            var transform = go.transform;
            var state = new ObjectState
            {
                LocalPosition = transform.localPosition,
                LocalRotation = transform.localRotation,
                LocalScale = transform.localScale,
                Active = go.activeSelf
            };

            var renderer = tracked.Renderer;
            if (renderer != null)
            {
                state.RendererEnabled = renderer.enabled;
                state.Material = renderer.sharedMaterial; // shared: reading .material would make a copy
                state.HasColor = state.Material != null && (state.Material.HasProperty("_BaseColor") || state.Material.HasProperty("_Color"));
                if (state.HasColor) state.Color = state.Material.color;
                if (renderer is SpriteRenderer sprite) state.SpriteColor = sprite.color;
            }

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

        private static void Apply(Tracked tracked, in ObjectState state)
        {
            var go = tracked.GameObject;
            var transform = go.transform;
            transform.localPosition = state.LocalPosition;
            transform.localRotation = state.LocalRotation;
            transform.localScale = state.LocalScale;
            if (go.activeSelf != state.Active) go.SetActive(state.Active);

            var renderer = tracked.Renderer;
            if (renderer != null)
            {
                renderer.enabled = state.RendererEnabled;
                // "change color" works on a per-object copy of the material (ADR-008). Pointing back at the material of
                // that moment undoes the swap; the copies themselves are freed by Unity's next unused-asset sweep.
                if (renderer.sharedMaterial != state.Material) renderer.sharedMaterial = state.Material;
                // Only a copy is ever recolored — never the object's original material, which in the Editor is the project asset itself.
                if (state.HasColor && state.Material != tracked.Start.Material) state.Material.color = state.Color;
                if (renderer is SpriteRenderer sprite) sprite.color = state.SpriteColor;
            }

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
