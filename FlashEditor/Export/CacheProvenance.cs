using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;

namespace FlashEditor.Export {
    /// <summary>Which revision-639 cache an export was taken from.</summary>
    public enum CacheKind {
        /// <summary>A 639 cache this project has not measured.</summary>
        Unrecognised,

        /// <summary>The vanilla live-server capture, OpenRS2 cache id 1194.</summary>
        VanillaB639,

        /// <summary>A private-server repack: a 639 base with local modifications.</summary>
        Repack
    }

    /// <summary>
    ///     Recognises which revision-639 cache is open, so an export can say what it is an export of.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Two 639 caches are supported and they disagree on eleven indexes, so a figure taken out of
    ///     an export means nothing until the export says which cache produced it. Six of the eleven
    ///     differ in their declared counts alone.
    ///     </para>
    ///     <para>
    ///     Recognition is by measured content, never by directory name: a cache copied, renamed or
    ///     opened through the environment override must still be recognised as what it is.
    ///     Reference-table <em>versions</em> are deliberately not consulted - index 3 carries version
    ///     1131 in both caches while holding 11 more groups and 1373 more files in the repack, so a
    ///     matching version is not evidence that an index is untouched. Declared group and file
    ///     counts on three indexes are read instead, so a single edit cannot make one cache
    ///     masquerade as the other.
    ///     </para>
    ///     <para>
    ///     A cache matching neither fingerprint is <see cref="CacheKind.Unrecognised"/> rather than an
    ///     error. The export still runs; its header carries the fingerprint it measured, which is
    ///     what a reader needs to tell two unrecognised caches apart.
    ///     </para>
    ///     <para>
    ///     This mirrors <c>FlashEditor.Tests.Cache.RealCache.RealCacheProfile</c> in shape and in the
    ///     two fingerprints it matches. It is a separate type rather than a shared one because the
    ///     test profile also carries every figure the suite asserts, none of which an export needs,
    ///     and because the test project references the production project rather than the reverse.
    ///     </para>
    /// </remarks>
    public sealed class CacheProvenance {
        /// <summary>Indexes whose declared counts form the fingerprint, in fingerprint order.</summary>
        private static readonly int[] FingerprintIndexes = {
            RSConstants.INTERFACE_DEFINITIONS_INDEX, RSConstants.TEXTURES, RSConstants.ITEM_DEFINITIONS_INDEX
        };

        /// <summary>Index 3 groups and files, index 9 groups and files, index 19 groups and files.</summary>
        private static readonly int[] VanillaFingerprint = { 1067, 40883, 915, 915, 80, 20427 };

        /// <summary>The same six figures, as the repack declares them.</summary>
        private static readonly int[] RepackFingerprint = { 1078, 42256, 946, 946, 80, 20470 };

        private CacheProvenance(CacheKind kind, string name, IReadOnlyList<int> fingerprint) {
            Kind = kind;
            Name = name;
            Fingerprint = fingerprint;
        }

        /// <summary>Which cache this is.</summary>
        public CacheKind Kind { get; }

        /// <summary>Human-readable name, for the export header and for log lines.</summary>
        public string Name { get; }

        /// <summary>
        ///     The six declared counts the recognition was taken from, in
        ///     <see cref="FingerprintIndexes"/> order, each index's group count then its file count.
        /// </summary>
        /// <remarks>
        ///     Written into the export whatever the verdict. An unrecognised cache is then still
        ///     identifiable to whoever reads the file later, and a recognised one can be checked
        ///     rather than believed.
        /// </remarks>
        public IReadOnlyList<int> Fingerprint { get; }

        /// <summary>The indexes the fingerprint is measured on, in the order the figures appear.</summary>
        public static IReadOnlyList<int> FingerprintedIndexes => FingerprintIndexes;

        /// <summary>Recognises the cache behind an open <see cref="RSCache"/>.</summary>
        /// <param name="cache">The open cache.</param>
        /// <returns>The provenance, never null.</returns>
        public static CacheProvenance Identify(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            return Identify(indexId => cache.GetReferenceTable(indexId));
        }

        /// <summary>Recognises a cache from the counts its reference tables declare.</summary>
        /// <param name="table">Resolves an index's decoded reference table.</param>
        /// <returns>The provenance, never null.</returns>
        public static CacheProvenance Identify(Func<int, RSReferenceTable> table) {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            var fingerprint = new List<int>();

            try {
                foreach (int indexId in FingerprintIndexes) {
                    RSReferenceTable declared = table(indexId);
                    fingerprint.Add(declared.GetArchiveCount());
                    fingerprint.Add(declared.GetArchiveEntries().Values.Sum(entry => entry.GetValidFileIds().Length));
                }
            } catch (Exception ex) {
                //A cache missing one of the fingerprint indexes is simply not one of the two this
                //project has measured. Failing to recognise it must not stop the export, or a cache
                //nobody has fingerprinted could not be exported at all.
                return new CacheProvenance(CacheKind.Unrecognised,
                    "a cache that could not be fingerprinted (" + ex.GetType().Name + ")",
                    Array.Empty<int>());
            }

            if (fingerprint.SequenceEqual(VanillaFingerprint))
                return new CacheProvenance(CacheKind.VanillaB639,
                    "the vanilla b639 capture (OpenRS2 1194)", fingerprint);

            if (fingerprint.SequenceEqual(RepackFingerprint))
                return new CacheProvenance(CacheKind.Repack, "the private-server repack", fingerprint);

            return new CacheProvenance(CacheKind.Unrecognised,
                "an unrecognised cache (" + string.Join("/", fingerprint) + ")", fingerprint);
        }
    }
}
