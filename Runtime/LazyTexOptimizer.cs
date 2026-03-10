using System.Collections.Generic;
using UnityEngine;

namespace LazyTex.Runtime
{
    public enum LazyTexAnalysisMode
    {
        Grayscale,
        Color,
    }

    /// <summary>
    /// アバターのルートまたは任意の子オブジェクトに追加することで LazyTex のテクスチャ最適化が有効になります。
    /// NDMFのOptimizingフェーズでSobel EERに基づくテクスチャ解像度削減を非破壊で適用します。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("LazyTex/LazyTex Optimizer")]
    public class LazyTexOptimizer : MonoBehaviour
    {
        [Tooltip("縮小後にBilinearで元サイズへ戻した画像とのEER下限値。\n" +
             "1/2, 1/4, 1/8... の各段階で同じ基準として使われ、\n" +
             "この値を下回った時点で1つ前の解像度を採用します。(0–1)")]
        [Range(0f, 1f)]
        public float eerThreshold = 0.90f;

        [Tooltip("Sobel EER の評価に使う入力モードです。\n" +
             "Grayscale は明暗ベース、Color は RGB の色差も含めて評価します。")]
        public LazyTexAnalysisMode analysisMode = LazyTexAnalysisMode.Color;

        [Tooltip("この短辺ピクセル数未満のテクスチャは処理をスキップします。\n" +
                 "小さすぎるテクスチャをさらに縮小しないための下限です。")]
        public int minResolutionToProcess = 512;

        [Tooltip("TextureImporterでNormalMapと設定されているテクスチャをスキップします。\n" +
                 "オフにすると曲率EERで縮小判定を行います。")]
        public bool skipNormalMaps = true;

        [Tooltip("ノーマルマップ用の曲率EER閾値。\n" +
                 "法線ベクトルの空間変化量（曲率）の保存率で縮小可否を判定します。(0\u20131)")]
        [Range(0f, 1f)]
        public float normalMapEerThreshold = 0.85f;

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
    }
}
