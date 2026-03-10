using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using nadena.dev.ndmf;
using LazyTex.Runtime;

namespace LazyTex.Editor
{
    /// <summary>
    /// NDMFビルドコンテキスト内でSobel EERによるテクスチャ解像度削減を実行します。
    /// 元アセット（Texture2D / Material）は一切変更しません。
    /// 削減が必要なテクスチャ・マテリアルはAssetContainerに保存したクローンで差し替えます。
    /// </summary>
    internal static class TextureOptimizePass
    {
        public static LazyTexRunReport Execute(BuildContext ctx, LazyTexOptimizer settings)
        {
            var avatarRoot = ctx.AvatarRootObject;
            var renderers  = avatarRoot.GetComponentsInChildren<Renderer>(true);
            var runReport = new LazyTexRunReport
            {
                AvatarName = avatarRoot.name,
                Timestamp = System.DateTime.Now,
                Threshold = settings.eerThreshold,
                NormalMapThreshold = settings.normalMapEerThreshold,
                AnalysisMode = settings.analysisMode,
                MinResolution = settings.minResolutionToProcess,
            };

            // --- Step 1: 全 (Renderer, slotIndex, Material, propName, Texture2D) を列挙 ---

            var entries = new List<TexEntry>();
            var texFactorMap = new Dictionary<Texture2D, int>();

            foreach (var renderer in renderers)
            {
                var mats = renderer.sharedMaterials;
                for (int slotIdx = 0; slotIdx < mats.Length; slotIdx++)
                {
                    var mat = mats[slotIdx];
                    if (mat == null || mat.shader == null) continue;

                    int propCount = mat.shader.GetPropertyCount();
                    for (int pi = 0; pi < propCount; pi++)
                    {
                        if (mat.shader.GetPropertyType(pi) !=
                            UnityEngine.Rendering.ShaderPropertyType.Texture) continue;

                        string propName = mat.shader.GetPropertyName(pi);
                        if (!(mat.GetTexture(propName) is Texture2D tex)) continue;

                        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex))) continue;

                        entries.Add(new TexEntry(renderer, slotIdx, mat, propName, tex));

                        if (!texFactorMap.ContainsKey(tex))
                            texFactorMap[tex] = 0;
                    }
                }
            }

            if (entries.Count == 0) return runReport;

            // --- Step 2: ユニークテクスチャごとにEERを計算して削減係数を決定 ---

            var resizedCache = new Dictionary<Texture2D, Texture2D>();
            var analysisMap = new Dictionary<Texture2D, LazyTexTextureReport>();
            var normalMapSet = new HashSet<Texture2D>();

            foreach (var tex in new List<Texture2D>(texFactorMap.Keys))
            {
                string texturePath = AssetDatabase.GetAssetPath(tex);
                bool isNormalMap = IsNormalMap(tex);
                LazyTexTextureReport analysis;
                if (settings.IsExcluded(texturePath))
                {
                    analysis = new LazyTexTextureReport
                    {
                        Texture = tex,
                        TexturePath = texturePath,
                        OriginalWidth = tex.width,
                        OriginalHeight = tex.height,
                        OriginalSizeBytes = TextureMemoryEstimate.GetEstimatedSizeBytes(tex),
                        ResizedSizeBytes = TextureMemoryEstimate.GetEstimatedSizeBytes(tex),
                        SelectedFactor = 1,
                        BestPassedSimilarity = 1f,
                        LastEvaluatedFactor = 1,
                        LastEvaluatedSimilarity = 1f,
                        Status = LazyTexTextureStatus.Excluded,
                        IsExcluded = true,
                        IsNormalMap = isNormalMap,
                    };
                }
                else if (isNormalMap && settings.skipNormalMaps)
                {
                    analysis = new LazyTexTextureReport
                    {
                        Texture = tex,
                        TexturePath = texturePath,
                        OriginalWidth = tex.width,
                        OriginalHeight = tex.height,
                        OriginalSizeBytes = TextureMemoryEstimate.GetEstimatedSizeBytes(tex),
                        ResizedSizeBytes = TextureMemoryEstimate.GetEstimatedSizeBytes(tex),
                        SelectedFactor = 1,
                        BestPassedSimilarity = 1f,
                        LastEvaluatedFactor = 1,
                        LastEvaluatedSimilarity = 1f,
                        Status = LazyTexTextureStatus.SkippedNormalMap,
                        IsExcluded = false,
                        IsNormalMap = true,
                    };
                }
                else if (isNormalMap)
                {
                    normalMapSet.Add(tex);
                    analysis = TextureEER.AnalyzeNormalMap(
                        tex,
                        settings.normalMapEerThreshold,
                        settings.minResolutionToProcess);
                    analysis.IsExcluded = false;
                }
                else
                {
                    analysis = TextureEER.AnalyzeTexture(
                        tex,
                        settings.eerThreshold,
                        settings.minResolutionToProcess,
                        settings.analysisMode);
                    analysis.IsExcluded = false;
                }

                analysisMap[tex] = analysis;
                runReport.Textures.Add(analysis);

                int factor = analysis.SelectedFactor;

                texFactorMap[tex] = factor;

                if (factor <= 1) continue;

                var resized = normalMapSet.Contains(tex)
                    ? TextureEER.CreateResizedNormalMap(tex, factor)
                    : TextureEER.CreateResizedTexture(tex, factor);
                analysis.ResizedTexture = resized;
                analysis.ResizedSizeBytes = TextureMemoryEstimate.GetEstimatedSizeBytes(resized);
                AssetDatabase.AddObjectToAsset(resized, ctx.AssetContainer);
                resizedCache[tex] = resized;
            }

            foreach (var entry in entries)
            {
                if (analysisMap.TryGetValue(entry.Tex, out var usageReport))
                {
                    usageReport.ReferenceCount++;
                }
            }

            if (resizedCache.Count == 0) return runReport;

            // --- Step 3: 影響するマテリアルをクローンしてテクスチャ参照を差し替え ---

            var matCloneCache = new Dictionary<Material, Material>();
            var rendererMatUpdates = new Dictionary<Renderer, Material[]>();

            foreach (var e in entries)
            {
                if (!resizedCache.TryGetValue(e.Tex, out var resizedTex)) continue;

                if (!matCloneCache.TryGetValue(e.Mat, out var clonedMat))
                {
                    clonedMat = Object.Instantiate(e.Mat);
                    clonedMat.name = e.Mat.name;
                    AssetDatabase.AddObjectToAsset(clonedMat, ctx.AssetContainer);
                    matCloneCache[e.Mat] = clonedMat;
                }

                clonedMat.SetTexture(e.PropName, resizedTex);

                if (!rendererMatUpdates.TryGetValue(e.Renderer, out var matArray))
                {
                    matArray = (Material[])e.Renderer.sharedMaterials.Clone();
                    rendererMatUpdates[e.Renderer] = matArray;
                }
                matArray[e.SlotIndex] = clonedMat;
            }

            // --- Step 4: Renderer に更新済みマテリアル配列を適用 ---
            foreach (var pair in rendererMatUpdates)
                pair.Key.sharedMaterials = pair.Value;

            Debug.Log($"[LazyTex] {resizedCache.Count} texture(s) resized on '{avatarRoot.name}':");
            foreach (var pair in resizedCache)
            {
                int f = texFactorMap[pair.Key];
                Debug.Log($"  {pair.Key.name} ({pair.Key.width}x{pair.Key.height})" +
                          $" → 1/{f} ({pair.Key.width / f}x{pair.Key.height / f})");
            }

            return runReport;
        }

        private static bool IsNormalMap(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return importer != null &&
                   importer.textureType == TextureImporterType.NormalMap;
        }

        private readonly struct TexEntry
        {
            public readonly Renderer  Renderer;
            public readonly int       SlotIndex;
            public readonly Material  Mat;
            public readonly string    PropName;
            public readonly Texture2D Tex;

            public TexEntry(Renderer r, int s, Material m, string p, Texture2D t)
            {
                Renderer  = r;
                SlotIndex = s;
                Mat       = m;
                PropName  = p;
                Tex       = t;
            }
        }
    }
}
