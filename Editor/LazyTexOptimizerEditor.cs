using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using LazyTex.Runtime;

namespace LazyTex.Editor
{
    [CustomEditor(typeof(LazyTexOptimizer))]
    internal sealed class LazyTexOptimizerEditor : UnityEditor.Editor
    {
        private const string ExcludedTexturesFoldoutKey = "LazyTexOptimizerEditor.ExcludedTexturesFoldout";
        private const string PresetHelpExpandedKey = "LazyTexOptimizerEditor.PresetHelpExpanded";
        private const string LanguageKey = "LazyTexOptimizerEditor.UseJapanese";
        private static readonly int[] MinResolutionOptions = { 128, 256, 512, 1024, 2048 };

        private static bool UseJapanese
        {
            get => EditorPrefs.GetBool(LanguageKey, true);
            set => EditorPrefs.SetBool(LanguageKey, value);
        }

        private static class L
        {
            private static bool JP => UseJapanese;

            public static string ToggleLanguageButton => JP ? "EN" : "JP";
            public static string ToggleLanguageTooltip => JP ? "Inspectorの表示言語を切り替えます" : "Switch the Inspector language";
            public static string PresetHelpTooltip => JP ? "プリセット説明を表示" : "Show preset description";
            public static string QualityPreset => JP ? "品質プリセット" : "Quality Preset";
            public static string QualityPresetTooltip => JP
                ? "処理品質のプリセットです。High / Medium / Low はしきい値を自動設定し、手動で変更すると Custom になります。"
                : "Quality presets. High / Medium / Low set thresholds automatically, and manual changes switch to Custom.";
            public static string SkipNormalMaps => JP ? "ノーマルマップを除外" : "Skip Normal Maps";
            public static string SkipNormalMapsTooltip => JP
                ? "TextureImporter で NormalMap と設定されているテクスチャをスキップします。オフにすると曲率EERで判定します。"
                : "Skips textures marked as NormalMap in the TextureImporter. When off, they are evaluated with curvature EER.";
            public static string ColorEERThreshold => JP ? "カラーテクスチャの目標類似度" : "Color EER Threshold";
            public static string ColorEERThresholdTooltip => JP
                ? "縮小後に元サイズへ戻した画像との EER 下限値です。この値を下回った時点で一つ前の解像度を採用します。"
                : "Lower EER bound against the image restored to original size after downscaling. The previous resolution is kept when this value is not met.";
            public static string NormalMapCurvatureThreshold => JP ? "ノーマルマップの目標類似度" : "Normal Map Curvature Threshold";
            public static string NormalMapCurvatureThresholdTooltip => JP
                ? "ノーマルマップの曲率保持率の下限値です。法線ベクトルの空間変化量をどれだけ保てるかで判定します。"
                : "Lower bound for normal map curvature preservation. It judges how much spatial change in normal vectors is retained.";
            public static string MinResolutionToProcess => JP ? "最小処理解像度" : "Min Resolution To Process";
            public static string MinResolutionTooltip => JP
                ? "この解像度未満のテクスチャは処理をスキップします。小さなテクスチャをさらに縮小しないための下限です。"
                : "Textures smaller than this resolution are skipped. This is the lower bound to avoid shrinking already small textures further.";
            // public static string MinResolutionHelpBox => JP
            //     ? "この解像度未満のテクスチャは処理をスキップします。小さなテクスチャをさらに縮小しないための下限です。"
            //     : "Textures smaller than this resolution are skipped. This is the lower bound to avoid shrinking already small textures further.";

            public static string PresetHelpBox => JP
                ? "High：最も保守的な設定です。単純なマスクや要素の少ない一部のテクスチャのみがリサイズされます。\n\n" +
                  "Medium：Highより少し軽量化を意識した設定です。上記に加え、模様の少ない衣装などのテクスチャがリサイズされます。\n\n" +
                  "Low：本格的に容量を減らすことを目的とした設定です。大半のテクスチャが必要に応じてリサイズされます。\n\n" +
                  "Custom：自由に値を設定できる設定です。参考までに、Highは0.9/0.8、Mediumは0.6/0.5、Lowは0.4/0.3に設定されています。"
                : "High: Most conservative setting. Only simple masks and a few low-detail textures will be resized.\n\n" +
                  "Medium: Slightly more aggressive than High. Additionally resizes textures with sparse patterns such as some outfit textures.\n\n" +
                  "Low: Designed to significantly reduce file size. Most textures will be resized as needed.\n\n" +
                  "Custom: Set values freely. For reference, High is 0.9/0.8, Medium is 0.6/0.5, and Low is 0.4/0.3.";

            public static string ExcludedTexturesHelpBox => JP
                ? "体や顔などのテクスチャは、チェックを入れて処理対象から除外しておくことを推奨します。"
                : "It is recommended to exclude textures such as body and face by checking them.";

            public static string ExcludedTexturesFoldout(int count) => JP
                ? $"除外するテクスチャ ({count})"
                : $"Excluded Textures ({count})";

            public static string ExcludedTexturesCheckHelpBox => JP
                ? "チェックを入れたものは縮小対象から除外されます。"
                : "Checked textures will be excluded from resizing.";

            public static string NoTexturesFound => JP ? "対象となるテクスチャが見つかりません。" : "No target textures found.";
            public static string UnusedExclusions => JP ? "未使用の除外項目" : "Unused Exclusions";
            public static string RemoveButton => JP ? "削除" : "Remove";
            public static string ExcludeUndoLabel => JP ? "LazyTexテクスチャを除外" : "Exclude LazyTex Texture";
            public static string IncludeUndoLabel => JP ? "LazyTexテクスチャを含める" : "Include LazyTex Texture";
            public static string RemoveExclusionUndoLabel => JP ? "LazyTex除外を削除" : "Remove LazyTex Exclusion";
            public static string ChangePresetUndoLabel => JP ? "LazyTex品質プリセットを変更" : "Change LazyTex Quality Preset";
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent(L.ToggleLanguageButton, L.ToggleLanguageTooltip), GUILayout.Width(30f)))
            {
                UseJapanese = !UseJapanese;
            }
            EditorGUILayout.EndHorizontal();

            var presetProp = serializedObject.FindProperty("qualityPreset");
            var eerThresholdProp = serializedObject.FindProperty("eerThreshold");
            var skipNormalMapsProp = serializedObject.FindProperty("skipNormalMaps");
            var normalMapThresholdProp = serializedObject.FindProperty("normalMapEerThreshold");
            var minResolutionProp = serializedObject.FindProperty("minResolutionToProcess");
            bool isCustomPreset = presetProp.enumValueIndex == (int)LazyTexQualityPreset.Custom;
            bool presetHelpExpanded = SessionState.GetBool(PresetHelpExpandedKey, false);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(presetProp, new GUIContent(L.QualityPreset, L.QualityPresetTooltip));
            if (GUILayout.Button(new GUIContent("?", L.PresetHelpTooltip), GUILayout.Width(24f)))
            {
                presetHelpExpanded = !presetHelpExpanded;
                SessionState.SetBool(PresetHelpExpandedKey, presetHelpExpanded);
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                ApplyPresetToTargets((LazyTexQualityPreset)presetProp.enumValueIndex);
                serializedObject.Update();
                isCustomPreset = presetProp.enumValueIndex == (int)LazyTexQualityPreset.Custom;
            }

            if (presetHelpExpanded)
            {
                EditorGUILayout.HelpBox(L.PresetHelpBox, MessageType.Info);
            }

            EditorGUILayout.PropertyField(skipNormalMapsProp, new GUIContent(L.SkipNormalMaps, L.SkipNormalMapsTooltip));

            if (isCustomPreset)
            {
                EditorGUI.BeginChangeCheck();
                float eerThreshold = EditorGUILayout.Slider(new GUIContent(L.ColorEERThreshold, L.ColorEERThresholdTooltip), eerThresholdProp.floatValue, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    eerThresholdProp.floatValue = eerThreshold;
                    presetProp.enumValueIndex = (int)LazyTexQualityPreset.Custom;
                }

                if (!skipNormalMapsProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginChangeCheck();
                    float normalThreshold = EditorGUILayout.Slider(new GUIContent(L.NormalMapCurvatureThreshold, L.NormalMapCurvatureThresholdTooltip), normalMapThresholdProp.floatValue, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        normalMapThresholdProp.floatValue = normalThreshold;
                        presetProp.enumValueIndex = (int)LazyTexQualityPreset.Custom;
                    }
                    EditorGUI.indentLevel--;
                }
            }

            DrawMinResolutionDropdown(minResolutionProp);
            //EditorGUILayout.HelpBox(L.MinResolutionHelpBox, MessageType.None);

            serializedObject.ApplyModifiedProperties();

            var optimizer = (LazyTexOptimizer)target;
            var textures = LazyTexTextureUsageUtility.CollectTextures(optimizer.gameObject);

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(L.ExcludedTexturesHelpBox, MessageType.Info);
            bool expanded = SessionState.GetBool(ExcludedTexturesFoldoutKey, false);
            expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, L.ExcludedTexturesFoldout(textures.Count));
            SessionState.SetBool(ExcludedTexturesFoldoutKey, expanded);

            if (!expanded)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            EditorGUILayout.HelpBox(L.ExcludedTexturesCheckHelpBox, MessageType.None);

            if (textures.Count == 0)
            {
                EditorGUILayout.LabelField(L.NoTexturesFound, EditorStyles.miniLabel);
                DrawOrphanedExclusions(optimizer, textures);
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            foreach (var texture in textures)
            {
                string path = AssetDatabase.GetAssetPath(texture);
                bool excluded = optimizer.IsExcluded(path);

                EditorGUILayout.BeginHorizontal();
                bool next = EditorGUILayout.Toggle(excluded, GUILayout.Width(18f));
                EditorGUILayout.ObjectField(texture, typeof(Texture2D), false);
                EditorGUILayout.EndHorizontal();

                if (next == excluded) continue;

                Undo.RecordObject(optimizer, next ? L.ExcludeUndoLabel : L.IncludeUndoLabel);
                optimizer.SetExcluded(path, next);
                EditorUtility.SetDirty(optimizer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(optimizer);
            }

            DrawOrphanedExclusions(optimizer, textures);
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void ApplyPresetToTargets(LazyTexQualityPreset preset)
        {
            foreach (var currentTarget in targets)
            {
                var optimizer = (LazyTexOptimizer)currentTarget;
                Undo.RecordObject(optimizer, L.ChangePresetUndoLabel);
                optimizer.ApplyQualityPreset(preset);
                EditorUtility.SetDirty(optimizer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(optimizer);
            }
        }

        private static void DrawMinResolutionDropdown(SerializedProperty minResolutionProp)
        {
            int currentValue = minResolutionProp.intValue;
            int selectedIndex = GetClosestMinResolutionIndex(currentValue);

            EditorGUI.BeginChangeCheck();
            selectedIndex = EditorGUILayout.Popup(
                new GUIContent(L.MinResolutionToProcess, L.MinResolutionTooltip),
                selectedIndex,
                new[] { "128x128", "256x256", "512x512", "1024x1024", "2048x2048" });
            if (EditorGUI.EndChangeCheck())
            {
                minResolutionProp.intValue = MinResolutionOptions[selectedIndex];
            }
        }

        private static int GetClosestMinResolutionIndex(int value)
        {
            int closestIndex = 0;
            int smallestDistance = Mathf.Abs(MinResolutionOptions[0] - value);

            for (int index = 1; index < MinResolutionOptions.Length; index++)
            {
                int distance = Mathf.Abs(MinResolutionOptions[index] - value);
                if (distance >= smallestDistance) continue;

                smallestDistance = distance;
                closestIndex = index;
            }

            return closestIndex;
        }

        private static void DrawOrphanedExclusions(LazyTexOptimizer optimizer, List<Texture2D> textures)
        {
            var knownPaths = new HashSet<string>();
            foreach (var texture in textures)
            {
                string path = AssetDatabase.GetAssetPath(texture);
                if (!string.IsNullOrEmpty(path))
                {
                    knownPaths.Add(path);
                }
            }

            bool drewHeader = false;
            foreach (var path in optimizer.ExcludedTextureAssetPaths)
            {
                if (string.IsNullOrEmpty(path) || knownPaths.Contains(path)) continue;

                if (!drewHeader)
                {
                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField(L.UnusedExclusions, EditorStyles.miniBoldLabel);
                    drewHeader = true;
                }

                EditorGUILayout.BeginHorizontal();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                EditorGUILayout.ObjectField(texture, typeof(Texture2D), false);
                if (GUILayout.Button(L.RemoveButton, GUILayout.Width(64f)))
                {
                    Undo.RecordObject(optimizer, L.RemoveExclusionUndoLabel);
                    optimizer.SetExcluded(path, false);
                    EditorUtility.SetDirty(optimizer);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(optimizer);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
