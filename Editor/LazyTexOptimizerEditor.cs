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

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("eerThreshold"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("analysisMode"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("minResolutionToProcess"));

            var skipNormalMapsProp = serializedObject.FindProperty("skipNormalMaps");
            EditorGUILayout.PropertyField(skipNormalMapsProp);
            if (!skipNormalMapsProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("normalMapEerThreshold"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableTimingLogs"));

            serializedObject.ApplyModifiedProperties();

            var optimizer = (LazyTexOptimizer)target;
            var textures = LazyTexTextureUsageUtility.CollectTextures(optimizer.gameObject);

            EditorGUILayout.Space(8f);
            bool expanded = SessionState.GetBool(ExcludedTexturesFoldoutKey, true);
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
