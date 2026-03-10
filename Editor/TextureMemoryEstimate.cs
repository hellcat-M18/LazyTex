using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace LazyTex.Editor
{
    internal static class TextureMemoryEstimate
    {
        public static long GetEstimatedSizeBytes(Texture2D texture)
        {
            if (texture == null)
            {
                return 0L;
            }

            var format = NormalizeTextureFormat(texture.format);
            int width = Mathf.Max(1, texture.width);
            int height = Mathf.Max(1, texture.height);
            int mipCount = Mathf.Max(1, texture.mipmapCount);

            long totalBytes = 0L;
            for (int mip = 0; mip < mipCount; mip++)
            {
                totalBytes += ComputeMipmapSizeBytes(width, height, format);

                if (width == 1 && height == 1)
                {
                    break;
                }

                width = Mathf.Max(1, width >> 1);
                height = Mathf.Max(1, height >> 1);
            }

            return totalBytes;
        }

        private static long ComputeMipmapSizeBytes(int width, int height, TextureFormat format)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            if (GraphicsFormatUtility.IsCompressedFormat(format))
            {
                int blockWidth = (int)GraphicsFormatUtility.GetBlockWidth(format);
                int blockHeight = (int)GraphicsFormatUtility.GetBlockHeight(format);
                int blockSize = (int)GraphicsFormatUtility.GetBlockSize(format);
                int blockCountX = (width + blockWidth - 1) / blockWidth;
                int blockCountY = (height + blockHeight - 1) / blockHeight;
                return (long)blockCountX * blockCountY * blockSize;
            }

            return (long)GraphicsFormatUtility.ComputeMipmapSize(width, height, format);
        }

        private static TextureFormat NormalizeTextureFormat(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1Crunched:
                    return TextureFormat.DXT1;
                case TextureFormat.DXT5Crunched:
                    return TextureFormat.DXT5;
                case TextureFormat.ETC_RGB4Crunched:
                    return TextureFormat.ETC_RGB4;
                case TextureFormat.ETC2_RGBA8Crunched:
                    return TextureFormat.ETC2_RGBA8;
                default:
                    return format;
            }
        }
    }
}
