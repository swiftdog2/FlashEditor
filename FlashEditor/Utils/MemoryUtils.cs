using System.Buffers;

namespace FlashEditor.Utils
{
    /// <summary>
    /// Helpers for renting and returning pooled byte arrays to avoid
    /// large-object-heap allocations during cache I/O.
    /// </summary>
    internal static class MemoryUtils
    {
        /// <summary>Byte threshold above which arrays are rented from the shared pool.</summary>
        public const int LargeObjectThreshold = 85 * 1024;

        /// <summary>
        /// Returns a pooled array when <paramref name="length"/> exceeds the LOH threshold,
        /// otherwise allocates normally.
        /// </summary>
        /// <param name="length">The required array length.</param>
        /// <returns>A byte array of at least <paramref name="length"/> elements.</returns>
        public static byte[] Rent(int length)
        {
            return length >= LargeObjectThreshold
                ? ArrayPool<byte>.Shared.Rent(length)
                : new byte[length];
        }

        /// <summary>
        /// Returns a buffer to the shared pool if it was rented (i.e. meets the LOH threshold).
        /// </summary>
        /// <param name="buffer">The buffer to return.</param>
        public static void Return(byte[] buffer)
        {
            if (buffer != null && buffer.Length >= LargeObjectThreshold)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
