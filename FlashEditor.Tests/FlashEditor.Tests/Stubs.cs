using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FlashEditor
{
    public static class CompressionUtils
    {
        public static byte[] Gzip(byte[] bytes)
        {
            using var output = new MemoryStream();
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen:true))
            {
                gz.Write(bytes, 0, bytes.Length);
            }
            return output.ToArray();
        }

        public static byte[] Gunzip(byte[] bytes)
        {
            using var input = new MemoryStream(bytes);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gz.CopyTo(output);
            return output.ToArray();
        }

        public static byte[] Bzip2(byte[] bytes) => bytes;
        public static byte[] Bunzip2(byte[] bytes, int length) => bytes;
    }
}

namespace FlashEditor.Cache.Util
{
    public static class Whirlpool
    {
        public static byte[] GetHash(byte[] data)
        {
            using var sha = SHA512.Create();
            return sha.ComputeHash(data);
        }

        public static byte[] whirlpool(byte[] data, int off, int length)
        {
            using var sha = SHA512.Create();
            return sha.ComputeHash(data.AsSpan(off, length).ToArray());
        }
    }

    public static class RSA
    {
        public static JagStream Crypt(JagStream buffer, java.math.BigInteger modulus, java.math.BigInteger key)
        {
            return buffer;
        }
    }
}

namespace FlashEditor.utils
{
    public static class DebugUtil
    {
        public enum LOG_DETAIL { NONE = 0, BASIC = 1, ADVANCED = 2, INSANE = 3 }
        public static void Debug(string output) { }
        public static void Debug(string output, LOG_DETAIL level) { }
        public static void Debug2(string output, LOG_DETAIL level) { }
        public static void PrintByteArray(byte[] buffer) { }
        public static void PrintByteArray(byte[] buffer, int length) { }
        public static void WriteLine(string output) { }
        public static string ToBitString(byte b) => Convert.ToString(b, 2).PadLeft(8, '0');
        public static string ToBitString(short s) => Convert.ToString(s, 2).PadLeft(16, '0');
        public static string ToBitString(int i) => Convert.ToString(i, 2).PadLeft(32, '0');
    }
}

namespace java.math
{
    public class BigInteger { }
}
