using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;

namespace FlashEditor.Definitions.Natives {
    /// <summary>
    ///     The names of index 30's groups, recovered from the hashes its reference table stores.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Index 30 sets the identifiers flag and the cache ships no plaintext, so a group carries
    ///     <c>hash("&lt;os&gt;/&lt;arch&gt;/&lt;library&gt;&lt;ext&gt;")</c> and nothing else. The
    ///     name is the entire structure of this index - it is what says which operating system and
    ///     which architecture a binary is for - so without it the editor can only show a signed
    ///     integer.
    ///     </para>
    ///     <para>
    ///     All thirty-six were recovered by brute force against
    ///     <see cref="NameHasher.GetNameHash"/> and are committed here rather than re-derived. Each
    ///     is self-proving in the sense this project demands: a name is reported only where its hash
    ///     equals the stored identifier exactly, so a wrong entry names nothing rather than naming
    ///     the wrong group, and a renumbered cache is still named correctly because the id falls out
    ///     of the hash at load.
    ///     </para>
    ///     <para>
    ///     <b>The list is not generated from the client's own path rules, and must never be.</b>
    ///     <c>Class365.java:70-72</c> only ever emits <c>x86_64/</c> for a 64-bit host, while this
    ///     cache stores one group under <c>windows/x64/</c> - see
    ///     <see cref="NativeLibraryCensus"/>. Generating the candidates would have produced a name
    ///     that hashes to nothing and quietly lost that group.
    ///     </para>
    /// </remarks>
    public static class NativeLibraryNames {
        /// <summary>
        ///     Every candidate name, of which only exact hash matches are ever reported.
        /// </summary>
        /// <remarks>
        ///     Deliberately a flat list and not a map from group id, for the reason
        ///     <c>LoadingSpriteNames</c> is: the id a name lands on is decided by the cache, not by
        ///     this file. There is no linux sw3d for ppc, no macos jagdx or jagmisc and no linux
        ///     jagdx or jagmisc - that is the shipped shape rather than a gap in the recovery.
        /// </remarks>
        private static readonly string[] Candidates = {
            "windows/msjava/jagmisc.dll",
            "windows/x86/hw3d.dll",
            "windows/x86/jaggl.dll",
            "windows/x86/sw3d.dll",
            "windows/x86/jaclib.dll",
            "windows/x86/jagdx.dll",
            "windows/x86/jagmisc.dll",
            "windows/x86_64/hw3d.dll",
            "windows/x86_64/jaggl.dll",
            "windows/x86_64/jaclib.dll",
            "windows/x86_64/jagdx.dll",
            "windows/x86_64/sw3d.dll",
            //Not a typo and not to be corrected. Every other 64-bit Windows library above is under
            //x86_64/; this one is under x64/, which the 637 client never asks for, so a 64-bit
            //client cannot load jagmisc from this cache at all. The hash on disk is the fact.
            "windows/x64/jagmisc.dll",
            "macos/universal/libhw3d.dylib",
            "macos/universal/libjaggl.dylib",
            "macos/universal/libjaclib.dylib",
            "macos/universal/libsw3d.dylib",
            "macos/x86/libhw3d.dylib",
            "macos/x86/libjaggl.dylib",
            "macos/x86/libjaclib.dylib",
            "macos/x86/libsw3d.dylib",
            "macos/x86_64/libhw3d.dylib",
            "macos/x86_64/libjaggl.dylib",
            "macos/x86_64/libjaclib.dylib",
            "macos/x86_64/libsw3d.dylib",
            "macos/ppc/libhw3d.dylib",
            "macos/ppc/libjaggl.dylib",
            "macos/ppc/libjaclib.dylib",
            "linux/x86/libhw3d.so",
            "linux/x86/libjaggl.so",
            "linux/x86/libjaclib.so",
            "linux/x86/libsw3d.so",
            "linux/x86_64/libhw3d.so",
            "linux/x86_64/libjaggl.so",
            "linux/x86_64/libjaclib.so",
            "linux/x86_64/libsw3d.so"
        };

        private static readonly Dictionary<int, string> ByHash = BuildIndex();

        /// <summary>The candidate set, so a test can pin what this claims to know.</summary>
        public static IReadOnlyList<string> KnownNames => Candidates;

        /// <summary>The name whose hash is <paramref name="identifier"/>, when one is known.</summary>
        /// <param name="identifier">The identifier the reference table stores for the group.</param>
        /// <param name="name">The recovered name, or <c>null</c>.</param>
        /// <returns>Whether a candidate hashes to that identifier.</returns>
        public static bool TryGetName(int identifier, out string? name) {
            return ByHash.TryGetValue(identifier, out name);
        }

        /// <summary>
        ///     The name of an index-30 group in an open cache, or <c>null</c>.
        /// </summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="groupId">The group id.</param>
        /// <returns>The recovered name, or <c>null</c> when the group is absent or unrecognised.</returns>
        public static string? NameOf(RSCache cache, int groupId) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            RSArchiveEntry? entry = cache.GetReferenceTable(RSConstants.NATIVE_LIBRARIES).GetArchiveEntry(groupId);
            if (entry == null)
                return null;

            return TryGetName(entry.GetIdentifier(), out string? name) ? name : null;
        }

        private static Dictionary<int, string> BuildIndex() {
            var index = new Dictionary<int, string>(Candidates.Length);
            foreach (string candidate in Candidates)
                index[NameHasher.GetNameHash(candidate)] = candidate;
            return index;
        }
    }
}
