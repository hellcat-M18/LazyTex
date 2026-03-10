using System;
using UnityEditor;
using UnityEngine;
using LazyTex.Runtime;

namespace LazyTex.Editor
{
    /// <summary>
    /// ComputeShader 経由で Sobel / Curvature マグニチュード総和を GPU 上で算出します。
    /// グラディエントカーネルはスレッドグループ内 (8×8) で部分和へ畳み込み、
    /// Reduce カーネルが階層的に合算して最終値を返します。
    /// </summary>
    internal sealed class TextureEERCompute : IDisposable
    {
        private const int GradGroupSize = 64;   // 8*8
        private const int ReduceGroupSize = 256;

        private readonly ComputeShader _shader;
        private readonly int _kernelSobelGrayscale;
        private readonly int _kernelSobelColor;
        private readonly int _kernelCurvatureNormal;
        private readonly int _kernelCurvatureNormalDxt5nm;
        private readonly int _kernelReduce;

        // Ping-pong buffers for partial sums / reduction.
        private ComputeBuffer _bufferA;
        private ComputeBuffer _bufferB;
        private int _currentBufferCapacity;

        // Reusable single-element readback array.
        private readonly float[] _readback = new float[1];

        private TextureEERCompute(ComputeShader shader)
        {
            _shader = shader;
            _kernelSobelGrayscale      = shader.FindKernel("SobelGrayscale");
            _kernelSobelColor          = shader.FindKernel("SobelColor");
            _kernelCurvatureNormal     = shader.FindKernel("CurvatureNormal");
            _kernelCurvatureNormalDxt5nm = shader.FindKernel("CurvatureNormalDxt5nm");
            _kernelReduce              = shader.FindKernel("Reduce");
        }

        /// <summary>
        /// GPU ComputeShader が利用可能な場合にインスタンスを返します。
        /// 非対応環境やシェーダが見つからない場合は null を返します。
        /// </summary>
        public static TextureEERCompute TryCreate()
        {
            if (!SystemInfo.supportsComputeShaders) return null;

            var guids = AssetDatabase.FindAssets("LazyTexEER t:ComputeShader");
            if (guids.Length == 0) return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (shader == null) return null;

            return new TextureEERCompute(shader);
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        public double ComputeSobelSum(RenderTexture rt, int width, int height, LazyTexAnalysisMode mode)
        {
            int kernel = mode == LazyTexAnalysisMode.Color
                ? _kernelSobelColor
                : _kernelSobelGrayscale;
            return DispatchGradientAndReduce(kernel, rt, width, height);
        }

        public double ComputeCurvatureSum(RenderTexture rt, int width, int height, bool isDxt5nm)
        {
            int kernel = isDxt5nm ? _kernelCurvatureNormalDxt5nm : _kernelCurvatureNormal;
            return DispatchGradientAndReduce(kernel, rt, width, height);
        }

        // -----------------------------------------------------------------------
        // Internal
        // -----------------------------------------------------------------------

        private double DispatchGradientAndReduce(int kernel, RenderTexture rt, int width, int height)
        {
            int groupsX = (width  + 7) / 8;
            int groupsY = (height + 7) / 8;
            int numGroups = groupsX * groupsY;

            EnsureBuffers(numGroups);

            // --- dispatch gradient kernel ---
            _shader.SetTexture(kernel, "_InputTex", rt);
            _shader.SetInt("_Width",   width);
            _shader.SetInt("_Height",  height);
            _shader.SetInt("_GroupsX", groupsX);
            _shader.SetBuffer(kernel, "_PartialSums", _bufferA);
            _shader.Dispatch(kernel, groupsX, groupsY, 1);

            // --- hierarchical reduction ---
            return ReduceSum(numGroups);
        }

        private double ReduceSum(int count)
        {
            ComputeBuffer current = _bufferA;

            while (count > 1)
            {
                int outCount = (count + ReduceGroupSize - 1) / ReduceGroupSize;
                ComputeBuffer next = (current == _bufferA) ? _bufferB : _bufferA;

                _shader.SetInt("_ReduceCount", count);
                _shader.SetBuffer(_kernelReduce, "_ReduceInput",  current);
                _shader.SetBuffer(_kernelReduce, "_ReduceOutput", next);
                _shader.Dispatch(_kernelReduce, outCount, 1, 1);

                current = next;
                count = outCount;
            }

            current.GetData(_readback, 0, 0, 1);
            return _readback[0];
        }

        private void EnsureBuffers(int requiredCapacity)
        {
            if (_bufferA != null && _currentBufferCapacity >= requiredCapacity)
                return;

            _bufferA?.Release();
            _bufferB?.Release();

            _bufferA = new ComputeBuffer(requiredCapacity, sizeof(float));
            _bufferB = new ComputeBuffer(requiredCapacity, sizeof(float));
            _currentBufferCapacity = requiredCapacity;
        }

        public void Dispose()
        {
            _bufferA?.Release();
            _bufferB?.Release();
            _bufferA = null;
            _bufferB = null;
        }
    }
}
