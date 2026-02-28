using System;
using System.IO;
using System.Buffers;
using FlashEditor.Utils;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;

namespace FlashEditor {
    /// <summary>
    /// Provides GZip and BZip2 compression and decompression utilities
    /// used by the cache container layer.
    /// </summary>
    public static class CompressionUtils {
        /// <summary>Decompresses a GZip-compressed byte array.</summary>
        /// <param name="bytes">The compressed data.</param>
        /// <returns>The decompressed bytes.</returns>
        public static byte[] Gunzip(byte[] bytes) {
            using var input = new GZipInputStream(new MemoryStream(bytes), 4096);
            using var inflateStream = new MemoryStream();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
            try {
                StreamUtils.Copy(input, inflateStream, buffer);
            }
            finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            return inflateStream.ToArray();
        }

        /// <summary>Compresses a byte array using GZip.</summary>
        /// <param name="bytes">The data to compress.</param>
        /// <returns>The GZip-compressed bytes.</returns>
        public static byte[] Gzip(byte[] bytes) {
            using var deflateStream = new MemoryStream();
            using (var gz = new GZipOutputStream(deflateStream, 4096)) {
                using var js = new MemoryStream(bytes);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
                try {
                    StreamUtils.Copy(js, gz, buffer);
                }
                finally {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            return deflateStream.ToArray();
        }

        /// <summary>
        /// Decompresses a BZip2-compressed byte array. The cache stores BZip2
        /// data without the standard <c>BZh1</c> header, so it is prepended before
        /// decompression.
        /// </summary>
        /// <param name="bytes">The compressed data (without BZip2 header).</param>
        /// <param name="decompressedLength">Expected length of the decompressed output.</param>
        /// <returns>The decompressed bytes.</returns>
        public static byte[] Bunzip2(byte[] bytes, int decompressedLength) {
            // prepend BZh1 header
            byte[] bzip2 = new byte[bytes.Length + 4];
            bzip2[0] = (byte) 'B';
            bzip2[1] = (byte) 'Z';
            bzip2[2] = (byte) 'h';
            bzip2[3] = (byte) '1';
            Array.Copy(bytes, 0, bzip2, 4, bytes.Length);

            using var inputStream = new BZip2InputStream(new MemoryStream(bzip2));
            byte[] data = new byte[decompressedLength];
            int read = inputStream.Read(data, 0, decompressedLength);
            if (read < decompressedLength)
                throw new EndOfStreamException($"Expected {decompressedLength} bytes, got {read}");
            return data;
        }

        /// <summary>
        /// Compresses a byte array using BZip2 and strips the <c>BZh1</c> header
        /// to match the cache container format.
        /// </summary>
        /// <param name="bytes">The data to compress.</param>
        /// <returns>The BZip2-compressed bytes without the header.</returns>
        public static byte[] Bzip2(byte[] bytes) {
            using var ms = new MemoryStream();
            using (var bz = new BZip2OutputStream(ms, 1)) {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
                try {
                    bz.Write(bytes, 0, bytes.Length);
                }
                finally {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            byte[] full = ms.ToArray();
            // strip the "BZh1" header
            if (full.Length > 4)
                return SubArray(full, 4, full.Length - 4);
            return Array.Empty<byte>();
        }

        /// <summary>Extracts a sub-array from the given array.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="data">The source array.</param>
        /// <param name="index">The starting index.</param>
        /// <param name="length">The number of elements to copy.</param>
        /// <returns>A new array containing the specified range.</returns>
        public static T[] SubArray<T>(T[] data, int index, int length) {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }
    }
}
