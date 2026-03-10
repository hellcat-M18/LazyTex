using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using LazyTex.Runtime;

namespace LazyTex.Editor
{
    /// <summary>
    /// Sobel Edge Energy Ratio（EER）を用いてテクスチャの解像度削減可否を判定するコアモジュール。
    ///
    /// EER = sum(Sobel(縮小→再拡大)) / sum(Sobel(元画像))
    ///
    /// 値が1.0に近いほど縮小後もエッジが保持されており、削減しても問題ないことを示します。
    /// Python実装（texture_quality_tool.py の eer_basic）からの移植。
    /// </summary>
    internal static class TextureEER
    {
        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// テクスチャの縮小係数（1=そのまま / 2=半解像度 / 4=1/4解像度 ...）を決定します。
        /// 元画像と「縮小→Bilinearで元サイズに再拡大」した画像を比較し、
        /// EERが閾値を下回る直前の係数を採用します。
        /// </summary>
        public static LazyTexTextureReport AnalyzeTexture(
            Texture2D tex,
            float eerThreshold,
            int minResolution,
            LazyTexAnalysisMode analysisMode)
        {
            var report = new LazyTexTextureReport
            {
                Texture = tex,
                TexturePath = AssetDatabase.GetAssetPath(tex),
                OriginalWidth = tex.width,
                OriginalHeight = tex.height,
                OriginalSizeBytes = TextureMemoryEstimate.GetEstimatedSizeBytes(tex),
                SelectedFactor = 1,
                BestPassedSimilarity = 1f,
                LastEvaluatedFactor = 1,
                LastEvaluatedSimilarity = 1f,
                Status = LazyTexTextureStatus.KeptOriginal,
            };

            int minDim = Mathf.Min(tex.width, tex.height);
            if (minDim < minResolution)
            {
                report.Status = LazyTexTextureStatus.SkippedTooSmall;
                return report;
            }

            Color[] originalPixels = ReadTexturePixels(tex, tex.width, tex.height);
            if (originalPixels == null) return report;

            double originalSobelSum = SobelMagnitudeSum(originalPixels, tex.width, tex.height, analysisMode);
            if (originalSobelSum < 1e-6)
            {
                int flatFactor = 1;
                for (int factor = 2; minDim / factor >= minResolution; factor *= 2)
                {
                    report.Steps.Add(new LazyTexTextureStepReport
                    {
                        Factor = factor,
                        Width = Mathf.Max(1, tex.width / factor),
                        Height = Mathf.Max(1, tex.height / factor),
                        Similarity = 1f,
                        Passed = true,
                    });

                    flatFactor = factor;
                    if (factor > int.MaxValue / 2) break;
                }

                report.SelectedFactor = flatFactor;
                report.BestPassedSimilarity = 1f;
                report.LastEvaluatedFactor = flatFactor;
                report.LastEvaluatedSimilarity = 1f;
                report.Status = flatFactor > 1 ? LazyTexTextureStatus.Resized : LazyTexTextureStatus.KeptOriginal;
                return report;
            }

            int bestFactor = 1;
            float bestSimilarity = 1f;
            for (int factor = 2; minDim / factor >= minResolution; factor *= 2)
            {
                float eer = ComputeRoundTripEer(tex, originalSobelSum, factor, analysisMode);
                bool passed = eer >= eerThreshold;

                report.Steps.Add(new LazyTexTextureStepReport
                {
                    Factor = factor,
                    Width = Mathf.Max(1, tex.width / factor),
                    Height = Mathf.Max(1, tex.height / factor),
                    Similarity = eer,
                    Passed = passed,
                });

                report.LastEvaluatedFactor = factor;
                report.LastEvaluatedSimilarity = eer;

                if (!passed)
                {
                    break;
                }

                bestFactor = factor;
                bestSimilarity = eer;

                if (factor > int.MaxValue / 2) break;
            }

            report.SelectedFactor = bestFactor;
            report.BestPassedSimilarity = bestSimilarity;
            report.Status = bestFactor > 1 ? LazyTexTextureStatus.Resized : LazyTexTextureStatus.KeptOriginal;
            return report;
        }

        /// <summary>
        /// ノーマルマップの縮小係数を曲率 EER（法線ベクトルの空間勾配の保存率）で決定します。
        /// Sobel EER のカラー版と同じループ構造ですが、RGB の代わりにデコード済み法線の
        /// 空間微分（曲率）を比較することで、ディテールの潰れを直接検出します。
        /// </summary>
        public static LazyTexTextureReport AnalyzeNormalMap(
            Texture2D tex,
            float eerThreshold,
            int minResolution)
        {
            var report = new LazyTexTextureReport
            {
                Texture = tex,
                TexturePath = AssetDatabase.GetAssetPath(tex),
                OriginalWidth = tex.width,
                OriginalHeight = tex.height,
                OriginalSizeBytes = TextureMemoryEstimate.GetEstimatedSizeBytes(tex),
                SelectedFactor = 1,
                BestPassedSimilarity = 1f,
                LastEvaluatedFactor = 1,
                LastEvaluatedSimilarity = 1f,
                Status = LazyTexTextureStatus.KeptOriginal,
                IsNormalMap = true,
            };

            int minDim = Mathf.Min(tex.width, tex.height);
            if (minDim < minResolution)
            {
                report.Status = LazyTexTextureStatus.SkippedTooSmall;
                return report;
            }

            bool isDxt5nm = IsDxt5nmEncoded(tex);
            Vector3[] originalNormals = ReadNormalMapPixels(tex, tex.width, tex.height, isDxt5nm);
            if (originalNormals == null) return report;

            double originalCurvatureSum = CurvatureMagnitudeSum(originalNormals, tex.width, tex.height);
            if (originalCurvatureSum < 1e-6)
            {
                int flatFactor = 1;
                for (int factor = 2; minDim / factor >= minResolution; factor *= 2)
                {
                    report.Steps.Add(new LazyTexTextureStepReport
                    {
                        Factor = factor,
                        Width = Mathf.Max(1, tex.width / factor),
                        Height = Mathf.Max(1, tex.height / factor),
                        Similarity = 1f,
                        Passed = true,
                    });

                    flatFactor = factor;
                    if (factor > int.MaxValue / 2) break;
                }

                report.SelectedFactor = flatFactor;
                report.BestPassedSimilarity = 1f;
                report.LastEvaluatedFactor = flatFactor;
                report.LastEvaluatedSimilarity = 1f;
                report.Status = flatFactor > 1 ? LazyTexTextureStatus.Resized : LazyTexTextureStatus.KeptOriginal;
                return report;
            }

            int bestFactor = 1;
            float bestSimilarity = 1f;
            for (int factor = 2; minDim / factor >= minResolution; factor *= 2)
            {
                float eer = ComputeRoundTripCurvatureEer(tex, originalCurvatureSum, factor, isDxt5nm);
                bool passed = eer >= eerThreshold;

                report.Steps.Add(new LazyTexTextureStepReport
                {
                    Factor = factor,
                    Width = Mathf.Max(1, tex.width / factor),
                    Height = Mathf.Max(1, tex.height / factor),
                    Similarity = eer,
                    Passed = passed,
                });

                report.LastEvaluatedFactor = factor;
                report.LastEvaluatedSimilarity = eer;

                if (!passed)
                {
                    break;
                }

                bestFactor = factor;
                bestSimilarity = eer;

                if (factor > int.MaxValue / 2) break;
            }

            report.SelectedFactor = bestFactor;
            report.BestPassedSimilarity = bestSimilarity;
            report.Status = bestFactor > 1 ? LazyTexTextureStatus.Resized : LazyTexTextureStatus.KeptOriginal;
            return report;
        }

        /// <summary>
        /// テクスチャを (width/factor × height/factor) にリサイズした新しい Texture2D を返します。
        /// RenderTexture経由でBlit（非Readableテクスチャにも対応）し、元アセットは変更しません。
        /// </summary>
        public static Texture2D CreateResizedTexture(Texture2D source, int factor)
        {
            int tw = Mathf.Max(1, source.width / factor);
            int th = Mathf.Max(1, source.height / factor);

            var prevRT = RenderTexture.active;
            var previousFilterMode = source.filterMode;
            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            rt.filterMode = FilterMode.Bilinear;
            try
            {
                source.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                const bool hasMips = true;
                var newTex = new Texture2D(tw, th, TextureFormat.RGBA32, mipChain: hasMips, linear: false);
                newTex.ReadPixels(new Rect(0, 0, tw, th), 0, 0, recalculateMipMaps: false);
                newTex.name = source.name + $"_lazytex_div{factor}";
                CopyTextureSettings(source, newTex);
                newTex.Apply(updateMipmaps: hasMips, makeNoLongerReadable: false);
                CompressGeneratedTexture(source, newTex);
                SetStreamingMipMaps(newTex, true, 0);
                newTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return newTex;
            }
            finally
            {
                source.filterMode = previousFilterMode;
                RenderTexture.active = prevRT;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// ノーマルマップ用のリサイズ。Linear 空間で Blit し、法線ベクトルを再正規化します。
        /// </summary>
        public static Texture2D CreateResizedNormalMap(Texture2D source, int factor)
        {
            int tw = Mathf.Max(1, source.width / factor);
            int th = Mathf.Max(1, source.height / factor);

            bool isDxt5nm = IsDxt5nmEncoded(source);

            var prevRT = RenderTexture.active;
            var previousFilterMode = source.filterMode;
            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Bilinear;
            try
            {
                source.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                const bool hasMips = true;
                var newTex = new Texture2D(tw, th, TextureFormat.RGBA32, mipChain: hasMips, linear: true);
                newTex.ReadPixels(new Rect(0, 0, tw, th), 0, 0, recalculateMipMaps: false);
                newTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                RenormalizeNormalMap(newTex, isDxt5nm);
                newTex.name = source.name + $"_lazytex_div{factor}";
                CopyTextureSettings(source, newTex);
                newTex.Apply(updateMipmaps: hasMips, makeNoLongerReadable: false);
                CompressGeneratedTexture(source, newTex);
                SetStreamingMipMaps(newTex, true, 0);
                newTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return newTex;
            }
            finally
            {
                source.filterMode = previousFilterMode;
                RenderTexture.active = prevRT;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // -----------------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------------

        private static Color[] ReadTexturePixels(Texture source, int width, int height)
        {
            var prevRT = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt);

            try
            {
                return ReadRenderTexturePixels(rt, width, height);
            }
            finally
            {
                RenderTexture.active = prevRT;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Color[] ReadRenderTexturePixels(RenderTexture rt, int width, int height)
        {
            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;

            var tmp = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false);
            try
            {
                tmp.ReadPixels(new Rect(0, 0, width, height), 0, 0, recalculateMipMaps: false);
                tmp.Apply(updateMipmaps: false);
                return tmp.GetPixels();
            }
            finally
            {
                Object.DestroyImmediate(tmp);
                RenderTexture.active = prevRT;
            }
        }

        private static float ComputeRoundTripEer(Texture2D source, double originalSobelSum, int factor, LazyTexAnalysisMode analysisMode)
        {
            if (originalSobelSum < 1e-6)
            {
                return 1f;
            }

            int downW = Mathf.Max(1, source.width / factor);
            int downH = Mathf.Max(1, source.height / factor);

            var previousFilterMode = source.filterMode;
            var downRT = RenderTexture.GetTemporary(downW, downH, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var upRT = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);

            downRT.filterMode = FilterMode.Bilinear;
            upRT.filterMode = FilterMode.Bilinear;

            try
            {
                source.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, downRT);
                Graphics.Blit(downRT, upRT);

                var roundTripPixels = ReadRenderTexturePixels(upRT, source.width, source.height);
                double roundTripSobelSum = SobelMagnitudeSum(roundTripPixels, source.width, source.height, analysisMode);
                return (float)(roundTripSobelSum / originalSobelSum);
            }
            finally
            {
                source.filterMode = previousFilterMode;
                RenderTexture.ReleaseTemporary(downRT);
                RenderTexture.ReleaseTemporary(upRT);
            }
        }

        private static void CopyTextureSettings(Texture2D source, Texture2D destination)
        {
            destination.wrapMode = source.wrapMode;
            destination.wrapModeU = source.wrapModeU;
            destination.wrapModeV = source.wrapModeV;
            destination.wrapModeW = source.wrapModeW;
            destination.filterMode = source.filterMode;
            destination.anisoLevel = source.anisoLevel;
            destination.mipMapBias = source.mipMapBias;
        }

        private static void CompressGeneratedTexture(Texture2D source, Texture2D generated)
        {
            bool hasAlpha = HasNonOpaqueAlpha(generated);
            TextureFormat compressionFormat = ChooseCompressionFormat(source, generated.width, generated.height, hasAlpha);
            if (compressionFormat == TextureFormat.RGBA32)
            {
                return;
            }

            try
            {
                EditorUtility.CompressTexture(generated, compressionFormat, TextureCompressionQuality.Best);
            }
            catch
            {
                // 圧縮に失敗した場合は未圧縮のまま残す。
            }
        }

        private static TextureFormat ChooseCompressionFormat(Texture2D source, int width, int height, bool hasAlpha)
        {
            if (GraphicsFormatUtility.IsCompressedFormat(source.format)
                && !GraphicsFormatUtility.IsCrunchFormat(source.format)
                && IsFormatDimensionCompatible(source.format, width, height))
            {
                return source.format;
            }

            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android:
                case BuildTarget.iOS:
                    return TextureFormat.ASTC_6x6;

                default:
                    if (width % 4 == 0 && height % 4 == 0)
                    {
                        return hasAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1;
                    }

                    return TextureFormat.RGBA32;
            }
        }

        private static bool HasNonOpaqueAlpha(Texture2D texture)
        {
            if (!GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat))
            {
                return false;
            }

            var pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < byte.MaxValue)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFormatDimensionCompatible(TextureFormat format, int width, int height)
        {
            if (!GraphicsFormatUtility.IsCompressedFormat(format))
            {
                return true;
            }

            int blockWidth = (int)GraphicsFormatUtility.GetBlockWidth(format);
            int blockHeight = (int)GraphicsFormatUtility.GetBlockHeight(format);
            return width % blockWidth == 0 && height % blockHeight == 0;
        }

        private static void SetStreamingMipMaps(Texture2D texture, bool enabled, int priority)
        {
            using var serializedTexture = new SerializedObject(texture);
            var streamingMipmapsProperty = serializedTexture.FindProperty("m_StreamingMipmaps");
            var streamingMipmapsPriorityProperty = serializedTexture.FindProperty("m_StreamingMipmapsPriority");
            if (streamingMipmapsProperty != null)
            {
                streamingMipmapsProperty.boolValue = enabled;
            }

            if (streamingMipmapsPriorityProperty != null)
            {
                streamingMipmapsPriorityProperty.intValue = priority;
            }

            serializedTexture.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Color配列全画素の Sobel 勾配マグニチュードの総和を返します。
        /// 3×3 Sobelカーネル、エッジはREPLICATE（端複製）パディング。
        /// </summary>
        private static double SobelMagnitudeSum(Color[] pixels, int w, int h, LazyTexAnalysisMode analysisMode)
        {
            double sum = 0.0;
            for (int y = 0; y < h; y++)
            {
                int y0 = y > 0 ? y - 1 : 0;
                int y2 = y < h - 1 ? y + 1 : h - 1;

                for (int x = 0; x < w; x++)
                {
                    int x0 = x > 0 ? x - 1 : 0;
                    int x2 = x < w - 1 ? x + 1 : w - 1;

                    Color p00 = pixels[y0 * w + x0];
                    Color p01 = pixels[y0 * w + x];
                    Color p02 = pixels[y0 * w + x2];
                    Color p10 = pixels[y  * w + x0];
                    Color p12 = pixels[y  * w + x2];
                    Color p20 = pixels[y2 * w + x0];
                    Color p21 = pixels[y2 * w + x];
                    Color p22 = pixels[y2 * w + x2];

                    sum += ComputeSobelMagnitude(p00, p01, p02, p10, p12, p20, p21, p22, analysisMode);
                }
            }
            return sum;
        }

        private static double ComputeSobelMagnitude(
            Color p00,
            Color p01,
            Color p02,
            Color p10,
            Color p12,
            Color p20,
            Color p21,
            Color p22,
            LazyTexAnalysisMode analysisMode)
        {
            if (analysisMode == LazyTexAnalysisMode.Color)
            {
                Vector3 gx = -ToRgb(p00) + ToRgb(p02) - 2f * ToRgb(p10) + 2f * ToRgb(p12) - ToRgb(p20) + ToRgb(p22);
                Vector3 gy = -ToRgb(p00) - 2f * ToRgb(p01) - ToRgb(p02) + ToRgb(p20) + 2f * ToRgb(p21) + ToRgb(p22);
                return System.Math.Sqrt(gx.sqrMagnitude + gy.sqrMagnitude);
            }

            float g00 = ToLuminance(p00);
            float g01 = ToLuminance(p01);
            float g02 = ToLuminance(p02);
            float g10 = ToLuminance(p10);
            float g12 = ToLuminance(p12);
            float g20 = ToLuminance(p20);
            float g21 = ToLuminance(p21);
            float g22 = ToLuminance(p22);

            float gxGray = -g00 + g02 - 2f * g10 + 2f * g12 - g20 + g22;
            float gyGray = -g00 - 2f * g01 - g02 + g20 + 2f * g21 + g22;
            return System.Math.Sqrt(gxGray * gxGray + gyGray * gyGray);
        }

        private static float ToLuminance(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        private static Vector3 ToRgb(Color c)
        {
            return new Vector3(c.r, c.g, c.b);
        }

        // -----------------------------------------------------------------------
        // Normal map helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// ノーマルマップテクスチャのピクセルを Linear 空間で読み取り、デコード済み法線ベクトル配列を返します。
        /// </summary>
        private static Vector3[] ReadNormalMapPixels(Texture source, int width, int height, bool isDxt5nm)
        {
            var prevRT = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt);

            try
            {
                return ReadNormalMapFromRT(rt, width, height, isDxt5nm);
            }
            finally
            {
                RenderTexture.active = prevRT;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Vector3[] ReadNormalMapFromRT(RenderTexture rt, int width, int height, bool isDxt5nm)
        {
            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;

            var tmp = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true);
            try
            {
                tmp.ReadPixels(new Rect(0, 0, width, height), 0, 0, recalculateMipMaps: false);
                tmp.Apply(updateMipmaps: false);
                Color[] pixels = tmp.GetPixels();
                var normals = new Vector3[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    normals[i] = DecodeNormal(pixels[i], isDxt5nm);
                }
                return normals;
            }
            finally
            {
                Object.DestroyImmediate(tmp);
                RenderTexture.active = prevRT;
            }
        }

        private static Vector3 DecodeNormal(Color pixel, bool isDxt5nm)
        {
            float x, y;
            if (isDxt5nm)
            {
                x = pixel.a * 2f - 1f;
                y = pixel.g * 2f - 1f;
            }
            else
            {
                x = pixel.r * 2f - 1f;
                y = pixel.g * 2f - 1f;
            }
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y));
            float mag = Mathf.Sqrt(x * x + y * y + z * z);
            return mag > 1e-6f ? new Vector3(x / mag, y / mag, z / mag) : Vector3.forward;
        }

        /// <summary>
        /// UnityのNormalMapインポート設定でDXT5を使用している場合、
        /// X成分がAlpha、Y成分がGreenに格納されるDXT5nmエンコードとなります。
        /// </summary>
        private static bool IsDxt5nmEncoded(Texture2D tex)
        {
            return tex.format == TextureFormat.DXT5 || tex.format == TextureFormat.DXT5Crunched;
        }

        /// <summary>
        /// デコード済み法線ベクトル配列の Sobel 曲率マグニチュード総和を返します。
        /// カラーテクスチャの SobelMagnitudeSum と同じ 3×3 カーネルを、
        /// RGB ではなく法線ベクトルの XYZ に適用したものです。
        /// </summary>
        private static double CurvatureMagnitudeSum(Vector3[] normals, int w, int h)
        {
            double sum = 0.0;
            for (int y = 0; y < h; y++)
            {
                int y0 = y > 0 ? y - 1 : 0;
                int y2 = y < h - 1 ? y + 1 : h - 1;

                for (int x = 0; x < w; x++)
                {
                    int x0 = x > 0 ? x - 1 : 0;
                    int x2 = x < w - 1 ? x + 1 : w - 1;

                    Vector3 n00 = normals[y0 * w + x0];
                    Vector3 n01 = normals[y0 * w + x];
                    Vector3 n02 = normals[y0 * w + x2];
                    Vector3 n10 = normals[y  * w + x0];
                    Vector3 n12 = normals[y  * w + x2];
                    Vector3 n20 = normals[y2 * w + x0];
                    Vector3 n21 = normals[y2 * w + x];
                    Vector3 n22 = normals[y2 * w + x2];

                    Vector3 gx = -n00 + n02 - 2f * n10 + 2f * n12 - n20 + n22;
                    Vector3 gy = -n00 - 2f * n01 - n02 + n20 + 2f * n21 + n22;
                    sum += System.Math.Sqrt(gx.sqrMagnitude + gy.sqrMagnitude);
                }
            }
            return sum;
        }

        private static float ComputeRoundTripCurvatureEer(
            Texture2D source, double originalCurvatureSum, int factor, bool isDxt5nm)
        {
            if (originalCurvatureSum < 1e-6) return 1f;

            int downW = Mathf.Max(1, source.width / factor);
            int downH = Mathf.Max(1, source.height / factor);

            var previousFilterMode = source.filterMode;
            var downRT = RenderTexture.GetTemporary(downW, downH, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            var upRT = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);

            downRT.filterMode = FilterMode.Bilinear;
            upRT.filterMode = FilterMode.Bilinear;

            try
            {
                source.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, downRT);
                Graphics.Blit(downRT, upRT);

                var roundTripNormals = ReadNormalMapFromRT(upRT, source.width, source.height, isDxt5nm);
                double roundTripCurvatureSum = CurvatureMagnitudeSum(roundTripNormals, source.width, source.height);
                return (float)(roundTripCurvatureSum / originalCurvatureSum);
            }
            finally
            {
                source.filterMode = previousFilterMode;
                RenderTexture.ReleaseTemporary(downRT);
                RenderTexture.ReleaseTemporary(upRT);
            }
        }

        /// <summary>
        /// Blit でダウンサンプルされた法線マップの各ピクセルを再正規化します。
        /// Bilinear 補間後のベクトルは単位長でなくなるため、これを修正します。
        /// </summary>
        private static void RenormalizeNormalMap(Texture2D tex, bool isDxt5nm)
        {
            Color[] pixels = tex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = pixels[i];
                Vector3 n = DecodeNormal(p, isDxt5nm);
                if (isDxt5nm)
                {
                    pixels[i] = new Color(p.r, n.y * 0.5f + 0.5f, p.b, n.x * 0.5f + 0.5f);
                }
                else
                {
                    pixels[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, p.a);
                }
            }
            tex.SetPixels(pixels);
        }
    }
}
