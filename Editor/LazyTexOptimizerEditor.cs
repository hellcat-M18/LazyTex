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

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var presetProp = serializedObject.FindProperty("qualityPreset");
            var eerThresholdProp = serializedObject.FindProperty("eerThreshold");
            var skipNormalMapsProp = serializedObject.FindProperty("skipNormalMaps");
            var normalMapThresholdProp = serializedObject.FindProperty("normalMapEerThreshold");
            bool isCustomPreset = presetProp.enumValueIndex == (int)LazyTexQualityPreset.Custom;
            bool presetHelpExpanded = SessionState.GetBool(PresetHelpExpandedKey, false);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(presetProp);
            if (GUILayout.Button(new GUIContent("?", "プリセット説明を表示"), GUILayout.Width(24f)))
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
                EditorGUILayout.HelpBox(
                    "High：最も保守的な設定です。単純なマスクや要素の少ない一部のテクスチャのみがリサイズされます。\n\n" +
                    "Medium：Highより少し軽量化を意識した設定です。上記に加え、模様の少ない衣装などのテクスチャがリサイズされます。\n\n" +
                    "Low：本格的に容量を減らすことを目的とした設定です。大半のテクスチャが必要に応じてリサイズされます。\n\n" +
                    "Custom：自由に値を設定できる設定です。参考までに、Highは0.9/0.8、Mediumは0.6/0.5、Lowは0.4/0.3に設定されています。",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(skipNormalMapsProp);

            if (isCustomPreset)
            {
                EditorGUI.BeginChangeCheck();
                float eerThreshold = EditorGUILayout.Slider(new GUIContent("Color EER Threshold"), eerThresholdProp.floatValue, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    eerThresholdProp.floatValue = eerThreshold;
                    presetProp.enumValueIndex = (int)LazyTexQualityPreset.Custom;
                }

                if (!skipNormalMapsProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginChangeCheck();
                    float normalThreshold = EditorGUILayout.Slider(new GUIContent("Normal Map Curvature Threshold"), normalMapThresholdProp.floatValue, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        normalMapThresholdProp.floatValue = normalThreshold;
                        presetProp.enumValueIndex = (int)LazyTexQualityPreset.Custom;
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("minResolutionToProcess"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableTimingLogs"));

            serializedObject.ApplyModifiedProperties();

            var optimizer = (LazyTexOptimizer)target;
            var textures = LazyTexTextureUsageUtility.CollectTextures(optimizer.gameObject);

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox("体や顔などのテクスチャは、チェックを入れて処理対象から除外しておくことを推奨します。", MessageType.Info);
            bool expanded = SessionState.GetBool(ExcludedTexturesFoldoutKey, false);
            expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, $"Excluded Textures ({textures.Count})");
            SessionState.SetBool(ExcludedTexturesFoldoutKey, expanded);

            if (!expanded)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            EditorGUILayout.HelpBox("チェックを入れたものは縮小対象から除外されます。", MessageType.None);

            if (textures.Count == 0)
            {
                EditorGUILayout.LabelField("対象テクスチャが見つかりません。", EditorStyles.miniLabel);
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

                Undo.RecordObject(optimizer, next ? "Exclude LazyTex Texture" : "Include LazyTex Texture");
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
                Undo.RecordObject(optimizer, "Change LazyTex Quality Preset");
                optimizer.ApplyQualityPreset(preset);
                EditorUtility.SetDirty(optimizer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(optimizer);
            }
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
                    EditorGUILayout.LabelField("Unused Exclusions", EditorStyles.miniBoldLabel);
                    drewHeader = true;
                }

                EditorGUILayout.BeginHorizontal();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                EditorGUILayout.ObjectField(texture, typeof(Texture2D), false);
                if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                {
                    Undo.RecordObject(optimizer, "Remove LazyTex Exclusion");
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
