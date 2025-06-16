using System;
using System.Text;

namespace FlashEditor.Cache.Util
{
    /// <summary>
    /// Computes case-insensitive hashes used for cache file lookups.
    /// </summary>
    public static class NameHasher
    {
        // Windows-1252 (CP1252) matches the RuneScape cache’s legacy byte mapping.
        // On .NET Core / .NET 5+, you must register CodePagesEncodingProvider once:
        //   Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        private static readonly Encoding Cp1252 = Encoding.GetEncoding(1252);

        /// <summary>
        /// Calculates the hash value for a name using the cache’s algorithm.
        /// </summary>
        /// <param name="name">String to hash (will be lower‐cased).</param>
        /// <returns>The 32-bit hash.</returns>
        public static int GetNameHash(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));

            int hash = 0;
            foreach (char c in name.ToLowerInvariant())
            {
                byte b = Cp1252.GetBytes(new[] { c })[0];
                hash = b + ((hash << 5) - hash);
            }
            return hash;
        }
    }
}
