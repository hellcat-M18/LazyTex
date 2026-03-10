using System.Collections.Generic;
using UnityEngine;

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
    public class LazyTexOptimizer : MonoBehaviour
    {
         [Tooltip("推奨設定のプリセットです。\n" +
               "High / Medium / Low はしきい値を自動設定し、\n" +
               "手動でしきい値を変更すると Custom に切り替わります。")]
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

        [Tooltip("一時的な計測ログを有効化します。\n" +
             "実行ごとの所要時間と重いテクスチャをConsoleに出力します。")]
        public bool enableTimingLogs = false;

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
                    break;

                case LazyTexQualityPreset.Medium:
                    eerThreshold = 0.60f;
                    normalMapEerThreshold = 0.50f;
                    break;

                case LazyTexQualityPreset.Low:
                    eerThreshold = 0.40f;
                    normalMapEerThreshold = 0.30f;
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
