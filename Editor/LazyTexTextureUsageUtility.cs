using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LazyTex.Editor
{
    internal static class LazyTexTextureUsageUtility
    {
        internal static List<Texture2D> CollectTextures(GameObject root)
        {
            var textures = new List<Texture2D>();
            var seen = new HashSet<Texture2D>();
            if (root == null) return textures;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null) continue;

                    int propertyCount = material.shader.GetPropertyCount();
                    for (int index = 0; index < propertyCount; index++)
                    {
                        if (material.shader.GetPropertyType(index) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;

                        string propertyName = material.shader.GetPropertyName(index);
                        if (!(material.GetTexture(propertyName) is Texture2D texture)) continue;
                        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture))) continue;
                        if (!seen.Add(texture)) continue;

                        textures.Add(texture);
                    }
                }
            }

            textures.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return textures;
        }

        internal static HashSet<string> CollectTexturePaths(GameObject root)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var texture in CollectTextures(root))
            {
                string path = AssetDatabase.GetAssetPath(texture);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }
    }
}
