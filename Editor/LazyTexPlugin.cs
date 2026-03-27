using UnityEngine;
using UnityEditor;
using nadena.dev.ndmf;
using LazyTex.Runtime;

[assembly: ExportsPlugin(typeof(LazyTex.Editor.LazyTexPlugin))]

namespace LazyTex.Editor
{
    /// <summary>
    /// LazyTex の NDMFプラグインエントリポイント。
    /// BuildPhase.Optimizing でアバター上の LazyTexOptimizer コンポーネントを検出し、
    /// Sobel EERに基づくテクスチャ解像度削減を非破壊で実行します。
    /// </summary>
    public class LazyTexPlugin : Plugin<LazyTexPlugin>
    {
        public override string DisplayName  => "LazyTex";
        public override string QualifiedName => "tool.hellcat.lazy-tex";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing).Run("LazyTex: Optimize Texture Resolution", ctx =>
            {
                var avatarRoot = ctx.AvatarRootObject;
                var optimizers = avatarRoot.GetComponentsInChildren<LazyTexOptimizer>(true);
                if (optimizers.Length == 0) return;

                if (optimizers.Length > 1)
                {
                    Debug.LogError(
                        "[LazyTex] 複数の LazyTex Optimizer コンポーネントが検出されました。1つのアバターに対して1つまでしか使用できません。先頭のコンポーネントの設定を使用します。",
                        optimizers[0]);
                }

                var report = TextureOptimizePass.Execute(ctx, optimizers[0]);
                LazyTexReportStore.Publish(report, EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying);

                // ビルド後のアバターにコンポーネントを残さない
                foreach (var opt in optimizers)
                    Object.DestroyImmediate(opt);
            });
        }
    }
}
