// File: Cache/Util/Whirlpool.cs
using System;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace FlashEditor.Cache.Util
{
    /// <summary>
    /// Computes Whirlpool hashes (512-bit) using the NESSIE spec.
    /// </summary>
    public static class Whirlpool
    {
        /// <summary>
        /// Computes the 512-bit Whirlpool digest of <paramref name="data"/>.
        /// </summary>
        /// <param name="data">Input bytes to hash.</param>
        /// <returns>64-byte Whirlpool hash.</returns>
        public static byte[] ComputeHash(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            var digest = new WhirlpoolDigest();
            digest.BlockUpdate(data, 0, data.Length);

            byte[] result = new byte[digest.GetDigestSize()];
            digest.DoFinal(result, 0);
            return result;
        }

        /// <summary>
        /// Computes the Whirlpool digest and returns it as an uppercase hex string.
        /// </summary>
        /// <param name="data">Input bytes to hash.</param>
        /// <returns>128-char hex representation.</returns>
        public static string ComputeHashHex(byte[] data)
        {
            byte[] hash = ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
