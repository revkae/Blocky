using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>Which way an object's motion blocks move it (<see cref="ObjectProgramRunner.Motion"/>).</summary>
    public enum MotionPlane
    {
        /// <summary>2D for an object with a SpriteRenderer, a Collider2D or a Rigidbody2D; 3D for anything else.</summary>
        Auto,

        /// <summary>Forward is the object's blue (Z) axis, and turning is around up (Y) — a 3D world's ground.</summary>
        ThreeD,

        /// <summary>Forward is the object's red (X) axis, the way a sprite faces, and turning is around Z — a flat XY stage.</summary>
        TwoD
    }

    /// <summary>
    /// Where "forward" and "turn" point for one object. In a 3D scene an object walks along its Z axis and turns
    /// around Y; on a 2D stage (XY, the camera looking down Z) a sprite faces along X — Scratch's direction 90 — and
    /// turns around Z, so the same "move 10" and "turn right 15" do what a learner expects in either.
    /// </summary>
    public static class BlockyPlane
    {
        /// <summary>True when <paramref name="go"/> moves on the flat XY plane: set so on its runner, or (Auto) it has 2D parts.</summary>
        public static bool IsFlat(GameObject go)
        {
            if (go == null) return false;
            if (go.TryGetComponent<ObjectProgramRunner>(out var runner) && runner.Motion != MotionPlane.Auto) return runner.Motion == MotionPlane.TwoD;
            if (go.TryGetComponent<SpriteRenderer>(out _)) return true;
#if BLOCKY_PHYSICS2D // set by Blocky.Runtime.asmdef when the project has Unity's 2D physics module
            return go.TryGetComponent<Collider2D>(out _) || go.TryGetComponent<Rigidbody2D>(out _);
#else
            return false;
#endif
        }

        /// <summary>The way "move forward" goes: the object's X axis on a flat stage, its Z axis in 3D.</summary>
        public static Vector3 Forward(Transform transform, bool flat) => flat ? transform.right : transform.forward;

        /// <summary>The axis "turn" turns around: Z on a flat stage (positive is anticlockwise on screen), up in 3D.</summary>
        public static Vector3 TurnAxis(bool flat) => flat ? Vector3.forward : Vector3.up;
    }
}
