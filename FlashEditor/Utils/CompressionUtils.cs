using System;
using Ionic.BZip2;
using java.io;
using java.util.zip;

namespace FlashEditor {
    public static class CompressionUtils {
        public static byte[] Gunzip(byte[] bytes) {
            //Create the streams
            var inputStream = new GZIPInputStream(new ByteArrayInputStream(bytes));
            try {
                var outputStream = new ByteArrayOutputStream();
                try {
                    //Copy data between the streams
                    var buf = new byte[4096];
                    var len = 0;
                    while((len = inputStream.read(buf, 0, buf.Length)) != -1)
                        outputStream.write(buf, 0, len);
                } finally {
                    outputStream.close();
                }

                //Return the uncompressed bytes
                return outputStream.toByteArray();
            } finally {
                inputStream.close();
            }
        }

        internal static byte[] Gzip(byte[] bytes) {
            using(var output = new JagStream()) {
                using(var compressor = new Ionic.Zlib.GZipStream(output, Ionic.Zlib.CompressionMode.Compress, Ionic.Zlib.CompressionLevel.BestCompression))
                    compressor.Write(bytes, 0, bytes.Length);

                return output.ToArray();
            }
        }

        //Figuring this shit out took weeks fml
        public static byte[] Bunzip2(byte[] bytes, int decompressedLength) {
            //Prepare a new byte array with the bzip2 header at the start
            byte[] bzip2 = new byte[bytes.Length + 4];
            bzip2[0] = (byte) 'B';
            bzip2[1] = (byte) 'Z';
            bzip2[2] = (byte) 'h'; //Huffman encoding
            bzip2[3] = (byte) '1'; //100kB block size
            Array.Copy(bytes, 0, bzip2, 4, bytes.Length);

            BZip2InputStream inputStream = new BZip2InputStream(new JagStream(bzip2));
            byte[] data = new byte[decompressedLength];

            inputStream.Read(data, 0, decompressedLength);
            inputStream.Close();

            return data;
        }

        public static byte[] Bzip2(byte[] bytes) {
            using(var outStream = new JagStream(bytes.Length)) {
                using(var bz2 = new BZip2OutputStream(outStream)) {
                    bz2.Write(bytes, 2, bytes.Length - 2);
                    return outStream.ToArray();
                }
            }
        }
    }
}