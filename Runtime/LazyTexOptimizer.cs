using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace LazyTex.Runtime
{
    public enum LazyTexAnalysisMode
    {
        Grayscale,
        Color,
    }

    public enum LazyTexQualityPreset
    {
        High,
        Medium,
        Low,
        Custom,
    }

    /// <summary>
    /// アバターのルートまたは任意の子オブジェクトに追加することで LazyTex のテクスチャ最適化が有効になります。
    /// NDMFのOptimizingフェーズでSobel EERに基づくテクスチャ解像度削減を非破壊で適用します。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("LazyTex/LazyTex Optimizer")]
    public class LazyTexOptimizer : MonoBehaviour, IEditorOnly
    {
        [Tooltip("推奨設定のプリセットです。\n" +
                 "High / Medium / Low はしきい値と解像度の下限値を自動設定し、\n" +
                 "手動で値を変更すると Custom に切り替わります。")] 
        public LazyTexQualityPreset qualityPreset = LazyTexQualityPreset.High;

        [Tooltip("縮小後にBilinearで元サイズへ戻した画像とのEER下限値。\n" +
             "1/2, 1/4, 1/8... の各段階で同じ基準として使われ、\n" +
             "この値を下回った時点で1つ前の解像度を採用します。(0–1)")]
        [Range(0f, 1f)]
        public float eerThreshold = 0.90f;

        [HideInInspector]
        public LazyTexAnalysisMode analysisMode = LazyTexAnalysisMode.Color;

        [Tooltip("この短辺ピクセル数未満のテクスチャは処理をスキップします。\n" +
                 "小さすぎるテクスチャをさらに縮小しないための下限です。")]
        public int minResolutionToProcess = 512;

        [Tooltip("TextureImporterでNormalMapと設定されているテクスチャをスキップします。\n" +
                 "オフにすると曲率EERで縮小判定を行います。")]
        public bool skipNormalMaps = false;

        [Tooltip("ノーマルマップ用の曲率EER閾値。\n" +
                 "法線ベクトルの空間変化量（曲率）の保存率で縮小可否を判定します。(0\u20131)")]
        [Range(0f, 1f)]
        public float normalMapEerThreshold = 0.80f;

        [SerializeField]
        private List<string> excludedTextureAssetPaths = new List<string>();

        public IReadOnlyList<string> ExcludedTextureAssetPaths => excludedTextureAssetPaths;

        public bool IsExcluded(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) && excludedTextureAssetPaths.Contains(assetPath);
        }

        public void SetExcluded(string assetPath, bool excluded)
        {
            if (string.IsNullOrEmpty(assetPath)) return;

            if (excluded)
            {
                if (!excludedTextureAssetPaths.Contains(assetPath))
                {
                    excludedTextureAssetPaths.Add(assetPath);
                }

                return;
            }

            excludedTextureAssetPaths.RemoveAll(path => path == assetPath);
        }

        public void ApplyQualityPreset(LazyTexQualityPreset preset)
        {
            qualityPreset = preset;
            analysisMode = LazyTexAnalysisMode.Color;

            switch (preset)
            {
                case LazyTexQualityPreset.High:
                    eerThreshold = 0.90f;
                    normalMapEerThreshold = 0.80f;
                    minResolutionToProcess = 512;
                    break;

                case LazyTexQualityPreset.Medium:
                    eerThreshold = 0.60f;
                    normalMapEerThreshold = 0.50f;
                    minResolutionToProcess = 512;
                    break;

                case LazyTexQualityPreset.Low:
                    eerThreshold = 0.40f;
                    normalMapEerThreshold = 0.30f;
                    minResolutionToProcess = 256;
                    break;

                case LazyTexQualityPreset.Custom:
                default:
                    break;
            }
        }

        private void OnValidate()
        {
            analysisMode = LazyTexAnalysisMode.Color;

            if (qualityPreset != LazyTexQualityPreset.Custom)
            {
                ApplyQualityPreset(qualityPreset);
            }
        }
    }
}
