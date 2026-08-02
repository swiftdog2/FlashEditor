using System;
using System.Text;

namespace FlashEditor.Cache.Util
{
    /// <summary>
    /// Computes the case-insensitive name hashes cache reference tables are keyed by.
    /// </summary>
    /// <remarks>
    ///     <c>hash = hash * 31 + ch</c> over the lower-cased name, matching the client
    ///     (Class305.method3580). Index 5 is addressed exclusively this way: a map square is
    ///     found by hashing <c>m50_50</c>, not through any region-to-file table.
    /// </remarks>
    public static class NameHasher
    {
        /// <summary>
        ///     Windows-1252, or <c>null</c> when the platform cannot supply it.
        /// </summary>
        /// <remarks>
        ///     This used to be an unguarded <c>Encoding.GetEncoding(1252)</c> in a field
        ///     initialiser. On .NET 9 only ASCII, Latin1 and the UTF family are built in - code
        ///     page 1252 needs <c>System.Text.Encoding.CodePages</c>, which this project does not
        ///     reference and never registers - so that threw a <see cref="NotSupportedException"/>
        ///     inside the static constructor and took down every caller with a
        ///     <see cref="TypeInitializationException"/>.
        ///
        ///     It is only consulted above U+007F. Every name in the cache is ASCII, so the fast
        ///     path below covers all real input and the encoding is a courtesy for anything else.
        /// </remarks>
        private static readonly Encoding? Cp1252 = TryGetCp1252();

        private static Encoding? TryGetCp1252()
        {
            try
            {
                return Encoding.GetEncoding(1252);
            }
            catch (Exception)
            {
                //Absent code page. Non-ASCII input degrades to Latin-1, which agrees with
                //Windows-1252 everywhere except 0x80-0x9F.
                return null;
            }
        }

        /// <summary>
        /// Calculates the hash value for a name using the cache's algorithm.
        /// </summary>
        /// <param name="name">String to hash. Lower-cased first, as the client does.</param>
        /// <returns>The 32-bit hash, wrapping on overflow.</returns>
        public static int GetNameHash(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));

            unchecked
            {
                int hash = 0;
                foreach (char c in name.ToLowerInvariant())
                    hash = ByteValue(c) + ((hash << 5) - hash);
                return hash;
            }
        }

        private static int ByteValue(char c)
        {
            if (c < 0x80)
                return c;

            if (Cp1252 != null)
                return Cp1252.GetBytes(new[] { c })[0];

            return (byte) c;
        }
    }
}
