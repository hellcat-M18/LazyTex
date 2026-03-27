using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEngine;
using UnityEditor;
using LazyTex.Runtime;

namespace LazyTex.Editor
{
    /// <summary>
    /// NDMFプレビューシステム用のIRenderFilter実装。
    /// LazyTexOptimizerが配置されたアバター上のレンダラーに対して、
    /// テクスチャ解像度削減の結果をシーンビューでリアルタイムにプレビューします。
    /// </summary>
    internal class LazyTexPreviewFilter : IRenderFilter
    {
        internal static readonly TogglablePreviewNode EnableNode = TogglablePreviewNode.Create(
            () => "LazyTex",
            qualifiedName: "tool.hellcat.lazy-tex/Preview"
        );

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return EnableNode;
        }

        public bool IsEnabled(ComputeContext context)
        {
            return context.Observe(EnableNode.IsEnabled);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = new List<RenderGroup>();

            foreach (var root in context.GetAvatarRoots())
            {
                if (context.ActiveInHierarchy(root) is false) continue;

                var optimizers = context.GetComponentsInChildren<LazyTexOptimizer>(root, true);
                if (optimizers.Length == 0) continue;

                // 設定値の変更を監視して再計算をトリガー
                var opt = optimizers[0];
                context.Observe(opt);

                var renderers = context.GetComponentsInChildren<Renderer>(root, true)
                    .Where(r => r is MeshRenderer or SkinnedMeshRenderer)
                    .ToList();

                if (renderers.Count == 0) continue;

                // アバター全体を1グループとして扱い、Optimizerを紐付ける
                groups.Add(RenderGroup.For(renderers).WithData(opt));
            }

            return groups.ToImmutableList();
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            var settings = group.GetData<LazyTexOptimizer>();
            var pairs = proxyPairs.ToList();

            // テクスチャ参照の収集 (proxyレンダラー上で作業)
            var entries = new List<TexEntry>();
            var texSet = new HashSet<Texture2D>();

            foreach (var (original, proxy) in pairs)
            {
                var mats = proxy.sharedMaterials;
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

                        entries.Add(new TexEntry(proxy, slotIdx, mat, propName, tex));
                        texSet.Add(tex);
                    }
                }
            }

            if (entries.Count == 0)
                return Task.FromResult<IRenderFilterNode>(new LazyTexPreviewNode());

            // テクスチャ解析 & リサイズ
            var resizedCache = new Dictionary<Texture2D, Texture2D>();
            var normalMapSet = new HashSet<Texture2D>();

            using var gpuCompute = TextureEERCompute.TryCreate();

            foreach (var tex in texSet)
            {
                string texturePath = AssetDatabase.GetAssetPath(tex);
                bool isNormalMap = IsNormalMap(tex);

                if (settings.IsExcluded(texturePath)) continue;
                if (isNormalMap && settings.skipNormalMaps) continue;

                int minDim = Mathf.Min(tex.width, tex.height);
                if (minDim < settings.minResolutionToProcess) continue;

                LazyTexTextureReport analysis;
                if (isNormalMap)
                {
                    normalMapSet.Add(tex);
                    analysis = TextureEER.AnalyzeNormalMap(
                        tex, settings.normalMapEerThreshold,
                        settings.minResolutionToProcess, gpuCompute);
                }
                else
                {
                    analysis = TextureEER.AnalyzeTexture(
                        tex, settings.eerThreshold,
                        settings.minResolutionToProcess,
                        LazyTexAnalysisMode.Color, gpuCompute);
                }

                int factor = analysis.SelectedFactor;
                if (factor <= 1) continue;

                var resized = normalMapSet.Contains(tex)
                    ? TextureEER.CreateResizedNormalMap(tex, factor)
                    : TextureEER.CreateResizedTexture(tex, factor);

                resized.hideFlags = HideFlags.HideAndDontSave;
                resizedCache[tex] = resized;
            }

            if (resizedCache.Count == 0)
                return Task.FromResult<IRenderFilterNode>(new LazyTexPreviewNode());

            // マテリアルのクローンとテクスチャ差し替え
            var clonedMaterials = new List<Material>();
            var matCloneCache = new Dictionary<Material, Material>();
            var rendererMats = new Dictionary<Renderer, Material[]>();

            foreach (var e in entries)
            {
                if (!resizedCache.TryGetValue(e.Tex, out var resizedTex)) continue;

                if (!matCloneCache.TryGetValue(e.Mat, out var clonedMat))
                {
                    clonedMat = Object.Instantiate(e.Mat);
                    clonedMat.name = e.Mat.name + " (LazyTex Preview)";
                    clonedMat.hideFlags = HideFlags.HideAndDontSave;
                    matCloneCache[e.Mat] = clonedMat;
                    clonedMaterials.Add(clonedMat);
                }

                clonedMat.SetTexture(e.PropName, resizedTex);

                if (!rendererMats.TryGetValue(e.Renderer, out var matArray))
                {
                    matArray = (Material[])e.Renderer.sharedMaterials.Clone();
                    rendererMats[e.Renderer] = matArray;
                }
                matArray[e.SlotIndex] = clonedMat;
            }

            // プロキシレンダラーにマテリアルを適用
            foreach (var pair in rendererMats)
                pair.Key.sharedMaterials = pair.Value;

            return Task.FromResult<IRenderFilterNode>(
                new LazyTexPreviewNode(resizedCache.Values.ToList(), clonedMaterials));
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
            public readonly Renderer Renderer;
            public readonly int SlotIndex;
            public readonly Material Mat;
            public readonly string PropName;
            public readonly Texture2D Tex;

            public TexEntry(Renderer r, int s, Material m, string p, Texture2D t)
            {
                Renderer = r;
                SlotIndex = s;
                Mat = m;
                PropName = p;
                Tex = t;
            }
        }
    }

    /// <summary>
    /// プレビューフィルタのステートフルノード。
    /// Instantiateで生成されたリサイズ済みテクスチャとクローンマテリアルの寿命を管理します。
    /// </summary>
    internal class LazyTexPreviewNode : IRenderFilterNode
    {
        private readonly List<Texture2D> _resizedTextures;
        private readonly List<Material> _clonedMaterials;

        public RenderAspects WhatChanged => RenderAspects.Texture | RenderAspects.Material;

        public LazyTexPreviewNode()
        {
            _resizedTextures = new List<Texture2D>();
            _clonedMaterials = new List<Material>();
        }

        public LazyTexPreviewNode(List<Texture2D> resizedTextures, List<Material> clonedMaterials)
        {
            _resizedTextures = resizedTextures;
            _clonedMaterials = clonedMaterials;
        }

        public void Dispose()
        {
            foreach (var mat in _clonedMaterials)
                if (mat != null) Object.DestroyImmediate(mat);

            foreach (var tex in _resizedTextures)
                if (tex != null) Object.DestroyImmediate(tex);
        }
    }
}
