using System;
using System.IO;
using FlashEditor.utils;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace FlashEditor {
    public static class CompressionUtils {
        //Basically GZIPInputStream is expecting GZIP as an input stream
        //Took way too long to realise that shit lmfao
        //Then as you copy the data back out it is decompressed/inflated
        public static byte[] Gunzip(byte[] bytes) {
            DebugUtil.PrintByteArray(bytes);
            MemoryStream inflateStream = new MemoryStream();
            using(GZipInputStream gz = new GZipInputStream(new JagStream(bytes), 4096)) {
                byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
                try {
                    StreamUtils.Copy(gz, inflateStream, buffer);
                } finally {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            return inflateStream.ToArray();
        }

        public static byte[] Gzip(byte[] bytes) {
            MemoryStream deflateStream = new MemoryStream();
            using(GZipOutputStream gz = new GZipOutputStream(deflateStream, 4096)) {
                using(MemoryStream js = new MemoryStream(bytes)) {
                    byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
                    try {
                        StreamUtils.Copy(js, gz, buffer);
                    } finally {
                        System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }

            return deflateStream.ToArray();
        }

        //Figuring this shit out took weeks fml
        public static byte[] Bunzip2(byte[] bytes, int decompressedLength) {
            //Prepare a new byte array with the bzip2 header at the start

            byte[] bzip2 = new byte[bytes.Length + 4];
            bzip2[0] = (byte) 'B'; //66
            bzip2[1] = (byte) 'Z'; //90
            bzip2[2] = (byte) 'h'; //104, huffman
            bzip2[3] = (byte) '1'; //1, 100kB block size
            Array.Copy(bytes, 0, bzip2, 4, bytes.Length);

            DebugUtil.PrintByteArray(bzip2);
            BZip2InputStream inputStream = new BZip2InputStream(new JagStream(bzip2));

            byte[] data = new byte[decompressedLength];

            inputStream.Read(data, 0, decompressedLength);
            inputStream.Close();

            return data;
        }

        public static byte[] Bzip2(byte[] bytes) {
            using (JagStream source = new JagStream(bytes))
            using (JagStream target = new JagStream()) {
                // Use a block size of 1 to match Bunzip2 and then
                // strip the BZIP2 header that the stream writes.
                using (BZip2OutputStream bz = new BZip2OutputStream(target, 1)) {
                    byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
                    try {
                        StreamUtils.Copy(source, bz, buffer);
                    } finally {
                        System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                byte[] data = target.ToArray();
                // Remove the 4 byte "BZh1" header
                if (data.Length > 4)
                    return SubArray(data, 4, data.Length - 4);
                return Array.Empty<byte>();
            }
        }

        public static T[] SubArray<T>(this T[] data, int index, int length) {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }
    }
}
