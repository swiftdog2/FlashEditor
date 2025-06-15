using System.Buffers;

namespace FlashEditor.utils
{
    internal static class MemoryUtils
    {
        public const int LargeObjectThreshold = 85 * 1024;

        public static byte[] Rent(int length)
        {
            return length >= LargeObjectThreshold
                ? ArrayPool<byte>.Shared.Rent(length)
                : new byte[length];
        }

        public static void Return(byte[] buffer)
        {
            if (buffer != null && buffer.Length >= LargeObjectThreshold)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
