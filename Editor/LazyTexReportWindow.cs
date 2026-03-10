using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using LazyTex.Runtime;

namespace LazyTex.Editor
{
    internal enum LazyTexTextureStatus
    {
        Excluded,
        Resized,
        KeptOriginal,
        SkippedTooSmall,
        SkippedNormalMap,
    }

    [Serializable]
    internal sealed class LazyTexTextureStepReport
    {
        public int Factor;
        public int Width;
        public int Height;
        public float Similarity;
        public bool Passed;
    }

    [Serializable]
    internal sealed class LazyTexTextureReport
    {
        public Texture2D Texture;
        public Texture2D ResizedTexture;
        public string TexturePath;
        public int OriginalWidth;
        public int OriginalHeight;
        public long OriginalSizeBytes;
        public long ResizedSizeBytes;
        public int SelectedFactor = 1;
        public float BestPassedSimilarity = 1f;
        public int LastEvaluatedFactor = 1;
        public float LastEvaluatedSimilarity = 1f;
        public int ReferenceCount;
        public LazyTexTextureStatus Status;
        public bool IsExcluded;
        public bool IsNormalMap;
        public readonly List<LazyTexTextureStepReport> Steps = new List<LazyTexTextureStepReport>();

        public bool WasResized => Status == LazyTexTextureStatus.Resized;
        public long SavedBytes => WasResized ? Math.Max(0L, OriginalSizeBytes - ResizedSizeBytes) : 0L;
    }

    [Serializable]
    internal sealed class LazyTexRunReport
    {
        public string AvatarName;
        public DateTime Timestamp;
        public float Threshold;
        public float NormalMapThreshold;
        public LazyTexAnalysisMode AnalysisMode;
        public int MinResolution;
        public readonly List<LazyTexTextureReport> Textures = new List<LazyTexTextureReport>();

        public int ResizedCount
        {
            get
            {
                int count = 0;
                foreach (var texture in Textures)
                {
                    if (texture.WasResized) count++;
                }

                return count;
            }
        }

        public long TotalSavedBytes
        {
            get
            {
                long total = 0L;
                foreach (var texture in Textures)
                {
                    total += texture.SavedBytes;
                }

                return total;
            }
        }

        public long TotalOriginalTextureBytes
        {
            get
            {
                long total = 0L;
                foreach (var texture in Textures)
                {
                    total += Math.Max(0L, texture.OriginalSizeBytes);
                }

                return total;
            }
        }

        public float TotalSavedRatio
        {
            get
            {
                long totalOriginal = TotalOriginalTextureBytes;
                return totalOriginal > 0 ? (float)TotalSavedBytes / totalOriginal : 0f;
            }
        }
    }

    [InitializeOnLoad]
    internal static class LazyTexReportStore
    {
        internal static LazyTexRunReport LatestReport { get; private set; }

        static LazyTexReportStore()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static void Publish(LazyTexRunReport report, bool autoOpen)
        {
            LatestReport = report;
            if (!autoOpen) return;

            EditorApplication.delayCall += () =>
            {
                if (LatestReport == report)
                {
                    LazyTexReportWindow.ShowWindow();
                }
            };
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;

            LatestReport = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<LazyTexReportWindow>())
            {
                window.Close();
            }
        }
    }

    internal sealed class LazyTexReportWindow : EditorWindow
    {
        private const float LeftPanelWidth = 280f;
        private const float SplitterWidth = 2f;
        private const float PreviewHeight = 220f;
        private const string LanguageKey = "LazyTexOptimizerEditor.UseJapanese";

        private static bool UseJapanese
        {
            get => EditorPrefs.GetBool(LanguageKey, true);
            set => EditorPrefs.SetBool(LanguageKey, value);
        }

        private static class L
        {
            private static bool JP => UseJapanese;

            public static string ToggleLanguageButton => JP ? "EN" : "JP";
            public static string NoReportMessage => JP
                ? "まだ LazyTex の実行結果がありません。PlayMode に入るか、LazyTex が動くビルドを実行してください。"
                : "No LazyTex results yet. Enter Play Mode or run a build with LazyTex active.";
            public static string NoResizes => JP ? "リサイズなし" : "No resizes";
            public static string ModeLabel(string mode) => JP ? $"モード: {mode}" : $"Mode: {mode}";
            public static string ExcludedInInspector => JP ? "Inspectorで除外済み" : "Excluded in Inspector";
            public static string SelectTextureHint => JP ? "左のリストからテクスチャを選択してください。" : "Select a texture from the list on the left.";
            public static string DetailsSection => JP ? "詳細" : "Details";
            public static string TextureField => JP ? "テクスチャ" : "Texture";
            public static string GeneratedField => JP ? "生成済み" : "Generated";
            public static string PathField => JP ? "パス" : "Path";
            public static string StatusField => JP ? "ステータス" : "Status";
            public static string AnalysisField => JP ? "解析方法" : "Analysis";
            public static string ExcludedField => JP ? "除外済み" : "Excluded";
            public static string ConfigureInInspector => JP ? "Inspectorで設定" : "Configure in Inspector";
            public static string UsedSlotsField => JP ? "使用スロット数" : "Used Slots";
            public static string ResolutionMemorySection => JP ? "解像度 & メモリ" : "Resolution & Memory";
            public static string OriginalField => JP ? "元のサイズ" : "Original";
            public static string SelectedField => JP ? "選択サイズ" : "Selected";
            public static string SavedField => JP ? "削減量" : "Saved";
            public static string EerStepsSection => JP ? "EER解析ステップ" : "EER Analysis Steps";
            public static string FactorHeader => JP ? "倍率" : "Factor";
            public static string ResolutionHeader => JP ? "解像度" : "Resolution";
            public static string ResultHeader => JP ? "結果" : "Result";
            public static string PreviewSection => JP ? "プレビュー" : "Preview";
            public static string BeforeLabel => JP ? "変更前" : "Before";
            public static string AfterLabel => JP ? "変更後" : "After";
            public static string PreviewUnavailable => JP ? "プレビューが利用できません" : "Preview unavailable";
            public static string ResizedCountLabel(int count) => JP ? $"{count} 件リサイズ" : $"{count} resized";
            public static string SavedBytesLabel(string bytes, float ratio) => JP
                ? $"- {bytes} 削減 (全体の {ratio:F1}%)"
                : $"- {bytes} saved ({ratio:F1}% of avatar textures)";
            public static string GetStatusLabel(LazyTexTextureStatus status)
            {
                switch (status)
                {
                    case LazyTexTextureStatus.Excluded: return JP ? "除外済み" : "Excluded";
                    case LazyTexTextureStatus.Resized: return JP ? "リサイズ済み" : "Resized";
                    case LazyTexTextureStatus.KeptOriginal: return JP ? "変更なし" : "Kept";
                    case LazyTexTextureStatus.SkippedTooSmall: return JP ? "スキップ: 小さすぎる" : "Skipped: Too Small";
                    case LazyTexTextureStatus.SkippedNormalMap: return JP ? "スキップ: ノーマルマップ" : "Skipped: Normal Map";
                    default: return status.ToString();
                }
            }
            public static string GetSummaryLabel(LazyTexTextureReport texture)
            {
                if (texture.Status == LazyTexTextureStatus.Resized)
                {
                    string label = texture.IsNormalMap ? (JP ? "曲率EER" : "Curvature EER") : "EER";
                    return $"1/{texture.SelectedFactor} ({texture.OriginalWidth / texture.SelectedFactor}x{texture.OriginalHeight / texture.SelectedFactor}) | {label} {texture.BestPassedSimilarity:F4}";
                }
                if (texture.Steps.Count > 0)
                {
                    string label = texture.IsNormalMap ? (JP ? "曲率EER" : "Curvature EER") : "EER";
                    string stoppedAt = JP ? "停止: 1/" : "Stopped at 1/";
                    return $"{stoppedAt}{texture.LastEvaluatedFactor} | {label} {texture.LastEvaluatedSimilarity:F4}";
                }
                return GetStatusLabel(texture.Status);
            }
        }

        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _selectedKey;
        private GUIStyle _greenLabel;
        private GUIStyle _greenEmphasisLabel;
        private GUIStyle _mutedLabel;
        private GUIStyle _excludedLabel;
        private GUIStyle _excludedMiniLabel;
        private GUIStyle _listItemNormal;
        private GUIStyle _listItemSelected;
        private GUIStyle _savedMiniLabel;
        private GUIStyle _tabNameLabel;
        private GUIStyle _sectionTitleLabel;

        [MenuItem("Tools/LazyTex/Last Report")]
        internal static void ShowWindow()
        {
            var window = GetWindow<LazyTexReportWindow>("LazyTex Report");
            window.minSize = new Vector2(640f, 360f);
            window.Show();
            window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L.ToggleLanguageButton, GUILayout.Width(30f)))
            {
                UseJapanese = !UseJapanese;
            }
            EditorGUILayout.EndHorizontal();

            var report = LazyTexReportStore.LatestReport;
            if (report == null)
            {
                EditorGUILayout.HelpBox(L.NoReportMessage, MessageType.Info);
                return;
            }

            var visibleTextures = GetVisibleTextures(report);

            EditorGUILayout.BeginHorizontal();

            // ---- Left panel: file list ----
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftPanelWidth));
            DrawLeftPanel(report, visibleTextures);
            EditorGUILayout.EndVertical();

            // ---- Splitter ----
            GUILayout.Box(GUIContent.none, GUILayout.Width(SplitterWidth), GUILayout.ExpandHeight(true));

            // ---- Right panel: details ----
            EditorGUILayout.BeginVertical();
            DrawRightPanel(report, visibleTextures);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            DrawFooter(report);
        }

        // ================================================================
        //  Left panel
        // ================================================================

        private void DrawLeftPanel(LazyTexRunReport report, List<LazyTexTextureReport> visibleTextures)
        {
            // Header summary
            EditorGUILayout.LabelField("LazyTex Report", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(report.AvatarName, GetMutedLabelStyle());
            EditorGUILayout.LabelField(L.ModeLabel(report.AnalysisMode.ToString()), GetMutedLabelStyle());
            EditorGUILayout.Space(4f);

            // Texture list
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            if (visibleTextures.Count == 0)
            {
                GUILayout.Label(L.NoResizes, GetMutedLabelStyle());
            }
            else
            {
                foreach (var texture in visibleTextures)
                {
                    DrawListItem(texture, report);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawListItem(LazyTexTextureReport texture, LazyTexRunReport report)
        {
            string key = GetTextureKey(texture);
            bool selected = _selectedKey == key;

            var style = selected ? GetListItemSelectedStyle() : GetListItemNormalStyle();
            Rect rowRect = EditorGUILayout.GetControlRect(false, 44f);

            if (Event.current.type == EventType.Repaint)
            {
                style.Draw(rowRect, false, false, selected, false);
            }

            var tabStripeRect = new Rect(rowRect.x, rowRect.y, 4f, rowRect.height);
            var isExcluded = texture.IsExcluded;
            EditorGUI.DrawRect(tabStripeRect,
                isExcluded
                    ? new Color(0.45f, 0.45f, 0.45f, 0.9f)
                    : selected ? new Color(0.22f, 0.6f, 0.95f) : new Color(0.25f, 0.25f, 0.25f, 0.6f));

            if (isExcluded)
            {
                EditorGUI.DrawRect(new Rect(rowRect.x + 4f, rowRect.y, rowRect.width - 4f, rowRect.height), new Color(0f, 0f, 0f, 0.18f));
            }

            float textWidth = rowRect.width - 20f;
            var nameRect = new Rect(rowRect.x + 10f, rowRect.y + 4f, textWidth, 16f);
            GUI.Label(nameRect, GetTextureName(texture), isExcluded ? GetExcludedLabelStyle() : GetTabNameLabelStyle());

            float shareOfAvatar = report.TotalOriginalTextureBytes > 0
                ? (float)texture.SavedBytes / report.TotalOriginalTextureBytes
                : 0f;

            var infoRect = new Rect(rowRect.x + 10f, rowRect.y + 22f, textWidth, 14f);
            string infoText = isExcluded
                ? L.ExcludedInInspector
                : $"1/{texture.SelectedFactor}  |  -{EditorUtility.FormatBytes(texture.SavedBytes)} ({shareOfAvatar * 100f:F1}%)";
            GUI.Label(infoRect, infoText, isExcluded ? GetExcludedMiniLabelStyle() : GetSavedMiniLabelStyle());

            var selectRect = rowRect;
            if (GUI.Button(selectRect, GUIContent.none, GUIStyle.none))
            {
                _selectedKey = key;
                if (texture.Texture != null)
                {
                    Selection.activeObject = texture.Texture;
                }
                Repaint();
            }
        }

        // ================================================================
        //  Right panel
        // ================================================================

        private void DrawRightPanel(LazyTexRunReport report, List<LazyTexTextureReport> visibleTextures)
        {
            LazyTexTextureReport sel = FindSelected(visibleTextures);

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (sel == null)
            {
                EditorGUILayout.HelpBox(L.SelectTextureHint, MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            // ---- Title ----
            EditorGUILayout.LabelField(GetTextureName(sel), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(L.GetSummaryLabel(sel), GetMutedLabelStyle());
            EditorGUILayout.Space(4f);

            DrawPreviewSection(sel);

            EditorGUILayout.Space(8f);

            // ---- Overview section ----
            EditorGUILayout.LabelField(L.DetailsSection, GetSectionTitleLabelStyle());
            EditorGUILayout.BeginVertical("box");
            DrawField(L.TextureField, () => EditorGUILayout.ObjectField(sel.Texture, typeof(Texture2D), false));
            if (sel.ResizedTexture != null)
            {
                DrawField(L.GeneratedField, () => EditorGUILayout.ObjectField(sel.ResizedTexture, typeof(Texture2D), false));
            }
            DrawLabelPair(L.PathField, string.IsNullOrEmpty(sel.TexturePath) ? "<temporary>" : sel.TexturePath);
            DrawLabelPair(L.StatusField, L.GetStatusLabel(sel.Status));
            DrawLabelPair(L.AnalysisField, report.AnalysisMode.ToString());
            if (sel.IsExcluded)
            {
                DrawLabelPairStyled(L.ExcludedField, L.ConfigureInInspector, GetExcludedMiniLabelStyle());
            }
            DrawLabelPair(L.UsedSlotsField, sel.ReferenceCount.ToString());
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // ---- Resolution & Memory ----
            EditorGUILayout.LabelField(L.ResolutionMemorySection, GetSectionTitleLabelStyle());
            EditorGUILayout.BeginVertical("box");
            DrawLabelPair(L.OriginalField, $"{sel.OriginalWidth} × {sel.OriginalHeight}  ({EditorUtility.FormatBytes(sel.OriginalSizeBytes)})");
            int selW = sel.OriginalWidth / sel.SelectedFactor;
            int selH = sel.OriginalHeight / sel.SelectedFactor;
            DrawLabelPair(L.SelectedField, $"{selW} × {selH}  ({EditorUtility.FormatBytes(sel.ResizedSizeBytes)})  — 1/{sel.SelectedFactor}");

            float shareOfAvatar = report.TotalOriginalTextureBytes > 0
                ? (float)sel.SavedBytes / report.TotalOriginalTextureBytes
                : 0f;
            DrawLabelPairFullStyled(L.SavedField,
                $"-{EditorUtility.FormatBytes(sel.SavedBytes)}  ({shareOfAvatar * 100f:F1}% of avatar textures)",
                GetGreenEmphasisLabelStyle());
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // ---- EER Steps ----
            if (sel.Steps.Count > 0)
            {
                EditorGUILayout.LabelField(L.EerStepsSection, GetSectionTitleLabelStyle());
                EditorGUILayout.BeginVertical("box");

                // Header
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(L.FactorHeader, EditorStyles.miniBoldLabel, GUILayout.Width(50f));
                GUILayout.Label(L.ResolutionHeader, EditorStyles.miniBoldLabel, GUILayout.Width(100f));
                GUILayout.Label("EER", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                GUILayout.Label(L.ResultHeader, EditorStyles.miniBoldLabel, GUILayout.Width(50f));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                foreach (var step in sel.Steps)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"1/{step.Factor}", GUILayout.Width(50f));
                    GUILayout.Label($"{step.Width}×{step.Height}", GUILayout.Width(100f));
                    GUILayout.Label(step.Similarity.ToString("F4"), GUILayout.Width(80f));
                    GUILayout.Label(step.Passed ? "PASS" : "FAIL",
                        step.Passed ? GetGreenLabelStyle() : EditorStyles.label,
                        GUILayout.Width(50f));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        //  Helpers
        // ================================================================

        private LazyTexTextureReport FindSelected(List<LazyTexTextureReport> visibleTextures)
        {
            if (visibleTextures.Count > 0 && string.IsNullOrEmpty(_selectedKey))
            {
                _selectedKey = GetTextureKey(visibleTextures[0]);
            }

            foreach (var t in visibleTextures)
            {
                if (GetTextureKey(t) == _selectedKey) return t;
            }

            if (visibleTextures.Count > 0)
            {
                _selectedKey = GetTextureKey(visibleTextures[0]);
                return visibleTextures[0];
            }

            return null;
        }

        private void DrawPreviewSection(LazyTexTextureReport texture)
        {
            EditorGUILayout.LabelField(L.PreviewSection, GetSectionTitleLabelStyle());
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            DrawTexturePreviewPane(L.BeforeLabel, texture.Texture, texture.OriginalWidth, texture.OriginalHeight, texture.OriginalSizeBytes);
            GUILayout.Space(8f);
            DrawTexturePreviewPane(
                L.AfterLabel,
                texture.ResizedTexture,
                Mathf.Max(1, texture.OriginalWidth / texture.SelectedFactor),
                Mathf.Max(1, texture.OriginalHeight / texture.SelectedFactor),
                texture.ResizedSizeBytes);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter(LazyTexRunReport report)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(L.ResizedCountLabel(report.ResizedCount), GetMutedLabelStyle(), GUILayout.Width(80f));
            GUILayout.Space(8f);
            GUILayout.Label(
                L.SavedBytesLabel(EditorUtility.FormatBytes(report.TotalSavedBytes), report.TotalSavedRatio * 100f),
                GetGreenEmphasisLabelStyle(),
                GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawTexturePreviewPane(string label, Texture texture, int width, int height, long sizeBytes)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            Rect previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.12f));

            if (texture != null)
            {
                var fitted = FitRectInside(previewRect, texture.width, texture.height);
                EditorGUI.DrawPreviewTexture(fitted, texture, null, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.Label(previewRect, L.PreviewUnavailable, GetMutedCenteredLabelStyle());
            }

            EditorGUILayout.LabelField($"{width} × {height}", GetMutedLabelStyle());
            EditorGUILayout.LabelField(EditorUtility.FormatBytes(sizeBytes), GetMutedLabelStyle());
            EditorGUILayout.EndVertical();
        }

        private static Rect FitRectInside(Rect bounds, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return bounds;
            }

            float aspect = (float)width / height;
            float boundsAspect = bounds.width / bounds.height;

            if (aspect > boundsAspect)
            {
                float fittedHeight = bounds.width / aspect;
                float y = bounds.y + (bounds.height - fittedHeight) * 0.5f;
                return new Rect(bounds.x, y, bounds.width, fittedHeight);
            }

            float fittedWidth = bounds.height * aspect;
            float x = bounds.x + (bounds.width - fittedWidth) * 0.5f;
            return new Rect(x, bounds.y, fittedWidth, bounds.height);
        }

        private static void DrawLabelPair(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUILayout.LabelField(value);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawLabelPairStyled(string label, string value, GUIStyle style)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUILayout.LabelField(value, style);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawLabelPairFullStyled(string label, string value, GUIStyle style)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, style, GUILayout.Width(110f));
            EditorGUILayout.LabelField(value, style);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawField(string label, System.Action draw)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            draw();
            EditorGUILayout.EndHorizontal();
        }

        private List<LazyTexTextureReport> GetVisibleTextures(LazyTexRunReport report)
        {
            var visible = new List<LazyTexTextureReport>();
            foreach (var texture in report.Textures)
            {
                if (!texture.WasResized && !texture.IsExcluded) continue;
                visible.Add(texture);
            }

            visible.Sort((a, b) =>
            {
                if (a.IsExcluded != b.IsExcluded) return a.IsExcluded ? 1 : -1;
                int savedCompare = b.SavedBytes.CompareTo(a.SavedBytes);
                if (savedCompare != 0) return savedCompare;
                return string.Compare(GetTextureName(a), GetTextureName(b), StringComparison.OrdinalIgnoreCase);
            });

            if (visible.Count > 0 && string.IsNullOrEmpty(_selectedKey))
            {
                _selectedKey = GetTextureKey(visible[0]);
            }

            return visible;
        }

        private static string GetTextureKey(LazyTexTextureReport texture)
        {
            return !string.IsNullOrEmpty(texture.TexturePath)
                ? texture.TexturePath
                : texture.Texture != null ? texture.Texture.GetInstanceID().ToString() : Guid.NewGuid().ToString();
        }

        private static string GetTextureName(LazyTexTextureReport texture)
        {
            if (texture.Texture != null) return texture.Texture.name;
            if (!string.IsNullOrEmpty(texture.TexturePath)) return System.IO.Path.GetFileNameWithoutExtension(texture.TexturePath);
            return "<missing texture>";
        }

        // ---- Styles ----

        private GUIStyle GetGreenLabelStyle()
        {
            if (_greenLabel == null)
            {
                _greenLabel = new GUIStyle(EditorStyles.label);
                _greenLabel.normal.textColor = new Color(0.2f, 0.9f, 0.2f);
                _greenLabel.focused.textColor = _greenLabel.normal.textColor;
                _greenLabel.hover.textColor = _greenLabel.normal.textColor;
                _greenLabel.active.textColor = _greenLabel.normal.textColor;
            }
            return _greenLabel;
        }

        private GUIStyle GetMutedLabelStyle()
        {
            if (_mutedLabel == null)
            {
                _mutedLabel = new GUIStyle(EditorStyles.miniLabel);
            }
            return _mutedLabel;
        }

        private GUIStyle GetGreenEmphasisLabelStyle()
        {
            if (_greenEmphasisLabel == null)
            {
                _greenEmphasisLabel = new GUIStyle(GetGreenLabelStyle());
                _greenEmphasisLabel.fontSize = EditorStyles.label.fontSize + 1;
                _greenEmphasisLabel.fontStyle = FontStyle.Bold;
            }

            return _greenEmphasisLabel;
        }

        private GUIStyle GetExcludedLabelStyle()
        {
            if (_excludedLabel == null)
            {
                _excludedLabel = new GUIStyle(GetTabNameLabelStyle());
                _excludedLabel.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
                _excludedLabel.focused.textColor = _excludedLabel.normal.textColor;
                _excludedLabel.hover.textColor = _excludedLabel.normal.textColor;
                _excludedLabel.active.textColor = _excludedLabel.normal.textColor;
            }

            return _excludedLabel;
        }

        private GUIStyle GetExcludedMiniLabelStyle()
        {
            if (_excludedMiniLabel == null)
            {
                _excludedMiniLabel = new GUIStyle(GetSavedMiniLabelStyle());
                _excludedMiniLabel.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                _excludedMiniLabel.focused.textColor = _excludedMiniLabel.normal.textColor;
                _excludedMiniLabel.hover.textColor = _excludedMiniLabel.normal.textColor;
                _excludedMiniLabel.active.textColor = _excludedMiniLabel.normal.textColor;
            }

            return _excludedMiniLabel;
        }

        private GUIStyle GetSavedMiniLabelStyle()
        {
            if (_savedMiniLabel == null)
            {
                _savedMiniLabel = new GUIStyle(EditorStyles.miniLabel);
                _savedMiniLabel.fontSize = EditorStyles.miniLabel.fontSize + 1;
                _savedMiniLabel.normal.textColor = new Color(0.2f, 0.9f, 0.2f);
            }
            return _savedMiniLabel;
        }

        private GUIStyle GetTabNameLabelStyle()
        {
            if (_tabNameLabel == null)
            {
                _tabNameLabel = new GUIStyle(EditorStyles.label);
                _tabNameLabel.fontStyle = FontStyle.Bold;
                _tabNameLabel.clipping = TextClipping.Clip;
            }

            return _tabNameLabel;
        }

        private GUIStyle GetSectionTitleLabelStyle()
        {
            if (_sectionTitleLabel == null)
            {
                _sectionTitleLabel = new GUIStyle(EditorStyles.boldLabel);
                _sectionTitleLabel.fontSize = 12;
            }

            return _sectionTitleLabel;
        }

        private GUIStyle GetMutedCenteredLabelStyle()
        {
            var style = new GUIStyle(GetMutedLabelStyle())
            {
                alignment = TextAnchor.MiddleCenter
            };
            return style;
        }

        private GUIStyle GetListItemNormalStyle()
        {
            if (_listItemNormal == null)
            {
                _listItemNormal = new GUIStyle("box");
                _listItemNormal.margin = new RectOffset(0, 0, 0, 1);
                _listItemNormal.padding = new RectOffset(0, 0, 0, 0);
            }
            return _listItemNormal;
        }

        private GUIStyle GetListItemSelectedStyle()
        {
            if (_listItemSelected == null)
            {
                _listItemSelected = new GUIStyle("SelectionRect");
                _listItemSelected.margin = new RectOffset(0, 0, 0, 1);
                _listItemSelected.padding = new RectOffset(0, 0, 0, 0);
            }
            return _listItemSelected;
        }
    }
}
