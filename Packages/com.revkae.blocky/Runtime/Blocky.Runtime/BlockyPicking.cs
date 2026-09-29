using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Which object is under a point on the screen — for <c>when clicked</c> and for choosing an object to program.
    /// A 3D collider, a 2D collider, or a sprite with no collider at all: 2D sprites often have none, and a learner
    /// clicking the cat must get the cat.
    /// </summary>
    public static class BlockyPicking
    {
        /// <summary>The object under <paramref name="screenPosition"/> (pixels, origin bottom-left), or null.</summary>
        public static GameObject ObjectAt(Camera camera, Vector2 screenPosition)
        {
            if (camera == null) return null;
            var ray = camera.ScreenPointToRay(screenPosition);

            GameObject nearest = null;
            var nearestDistance = float.PositiveInfinity;
            if (Physics.Raycast(ray, out var hit))
            {
                nearest = hit.collider.gameObject;
                nearestDistance = hit.distance;
            }

#if BLOCKY_PHYSICS2D // set by Blocky.Runtime.asmdef when the project has Unity's 2D physics module
            var hit2D = Physics2D.GetRayIntersection(ray, Mathf.Infinity);
            if (hit2D.collider != null && hit2D.distance < nearestDistance) nearest = hit2D.collider.gameObject;
#endif

            return nearest != null ? nearest : SpriteAt(ray);
        }

        /// <summary>
        /// The topmost sprite whose bounds hold the point where <paramref name="ray"/> meets the sprite's plane — by
        /// sorting layer, then order in layer, then nearness, as they are drawn. Only asked when no collider was hit,
        /// and only on a press, so looking at every sprite is affordable.
        /// </summary>
        private static GameObject SpriteAt(Ray ray)
        {
            SpriteRenderer top = null;
            var topDistance = 0f;
            foreach (var sprite in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (!sprite.enabled) continue;

                var bounds = sprite.bounds;
                var plane = new Plane(Vector3.forward, bounds.center);
                if (!plane.Raycast(ray, out var distance)) continue;

                var point = ray.GetPoint(distance);
                if (point.x < bounds.min.x || point.x > bounds.max.x || point.y < bounds.min.y || point.y > bounds.max.y) continue;

                if (top == null || DrawnAbove(sprite, distance, top, topDistance))
                {
                    top = sprite;
                    topDistance = distance;
                }
            }
            return top != null ? top.gameObject : null;
        }

        private static bool DrawnAbove(SpriteRenderer a, float aDistance, SpriteRenderer b, float bDistance)
        {
            var layerA = SortingLayer.GetLayerValueFromID(a.sortingLayerID);
            var layerB = SortingLayer.GetLayerValueFromID(b.sortingLayerID);
            if (layerA != layerB) return layerA > layerB;
            if (a.sortingOrder != b.sortingOrder) return a.sortingOrder > b.sortingOrder;
            return aDistance < bDistance;
        }
    }
}
