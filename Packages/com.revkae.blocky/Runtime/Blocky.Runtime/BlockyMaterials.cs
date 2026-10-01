using System.Collections.Generic;
using UnityEngine;

// The statics here are fixed values that never change, so there is nothing to reset between Play sessions.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime
{
    /// <summary>
    /// What "change color" and Reset agree on: which renderers belong to an object's look, and where a material keeps
    /// its color. An imported model keeps its meshes under a root that has no renderer of its own, so an object's look
    /// is every renderer under it — except a speech bubble's, which is Blocky's, not the object's.
    /// </summary>
    internal static class BlockyMaterials
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
        private static readonly int ColorId = Shader.PropertyToID("_Color");         // built-in pipeline, sprites

        /// <summary>Fills <paramref name="into"/> with every renderer under <paramref name="go"/>, its own and inactive ones included.</summary>
        public static void RenderersUnder(GameObject go, List<Renderer> into)
        {
            into.Clear();
            if (go == null) return;

            go.GetComponentsInChildren(true, into);
            for (var i = into.Count - 1; i >= 0; i--)
                if (into[i].TryGetComponent<BlockyBubblePart>(out _)) into.RemoveAt(i);
        }

        /// <summary>The material's color, when its shader has one to set.</summary>
        public static bool TryGetColor(Material material, out Color color)
        {
            if (material != null)
            {
                if (material.HasProperty(BaseColorId))
                {
                    color = material.GetColor(BaseColorId);
                    return true;
                }

                if (material.HasProperty(ColorId))
                {
                    color = material.GetColor(ColorId);
                    return true;
                }
            }

            color = default;
            return false;
        }

        /// <summary>Sets whichever color properties the shader has — some (URP Lit) carry both.</summary>
        public static void SetColor(Material material, Color color)
        {
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        }
    }
}
