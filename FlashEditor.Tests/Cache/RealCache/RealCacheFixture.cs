using FlashEditor.cache;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     Opens the real revision-639 cache once for the whole conformance class and hands out
    ///     the captured bytes the tests assert against.
    /// </summary>
    /// <remarks>
    ///     Everything here reads through the production types - <see cref="RSCache.LoadContainer"/>
    ///     for the sector chain, <see cref="ReferenceTableCodec"/> for the tables - so a defect in
    ///     the reader shows up as a conformance failure rather than being papered over by a
    ///     second implementation living in the test project.
    /// </remarks>
    public sealed class RealCacheFixture : IDisposable
    {
        /// <summary>
        ///     Archives examined per index when not sweeping the whole cache. Sampling is by
        ///     stride rather than by prefix, so the whole id range is covered either way.
        /// </summary>
        public const int SampleArchivesPerIndex = 250;

        private readonly RSFileStore _store;
        private readonly RSCache _cache;
        private readonly Dictionary<int, RSReferenceTable> _tables = new Dictionary<int, RSReferenceTable>();

        /// <summary>Whether a cache was located and opened.</summary>
        public bool Available { get; }

        /// <summary>The meta-index group ids that hold a reference table.</summary>
        public IReadOnlyList<int> TableIndexes { get; } = Array.Empty<int>();

        /// <summary>Whether every archive is being examined rather than a per-index sample.</summary>
        public bool FullSweep => RealCacheLocator.FullSweep;

        /// <summary>Opens the cache, or leaves the fixture unavailable when there is none.</summary>
        public RealCacheFixture()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            if (RealCacheLocator.Directory == null)
                return;

            _store = new RSFileStore(RealCacheLocator.Directory);
            _cache = new RSCache(_store);

            //Map archives are XTEA encrypted. Without a key file they simply cannot be read,
            //so the conformance tests distinguish "no key available" from "key did not work".
            _cache.TryAutoLoadXTEAKeys(RealCacheLocator.Directory);
            Available = true;

            var indexes = new List<int>();
            int metaGroups = _store.GetFileCount(RSConstants.META_INDEX);
            for (int indexId = 0; indexId < metaGroups; indexId++)
            {
                byte[] raw = RawContainer(RSConstants.META_INDEX, indexId);
                if (raw == null)
                    continue;
                indexes.Add(indexId);
            }
            TableIndexes = indexes;
        }

        /// <summary>
        ///     The open cache, for tests that exercise a reader built on top of it rather than
        ///     comparing captured bytes.
        /// </summary>
        /// <returns>The cache opened by this fixture.</returns>
        public RSCache OpenCache() => _cache;

        /// <summary>
        ///     Returns the stored container bytes for an archive exactly as they sit in the dat2,
        ///     or <c>null</c> when the index record is empty.
        /// </summary>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <returns>The captured container bytes, or <c>null</c>.</returns>
        public byte[] RawContainer(int indexId, int archiveId)
        {
            JagStream stream = _cache.LoadContainer(indexId, archiveId);
            return stream?.ToArray();
        }

        /// <summary>
        ///     Decodes and memoises the reference table stored in meta-index group
        ///     <paramref name="indexId"/>.
        /// </summary>
        /// <param name="indexId">The meta-index group id.</param>
        /// <returns>The decoded reference table.</returns>
        public RSReferenceTable Table(int indexId)
        {
            if (_tables.TryGetValue(indexId, out RSReferenceTable cached))
                return cached;

            RSReferenceTable table = ReferenceTableCodec.Decode(new JagStream(TablePayload(indexId)));
            _tables[indexId] = table;
            return table;
        }

        /// <summary>
        ///     Returns the decompressed reference-table payload for meta-index group
        ///     <paramref name="indexId"/> - the bytes the table codec is expected to reproduce.
        /// </summary>
        /// <param name="indexId">The meta-index group id.</param>
        /// <returns>The container payload holding the encoded table.</returns>
        public byte[] TablePayload(int indexId)
        {
            byte[] raw = RawContainer(RSConstants.META_INDEX, indexId);
            RSContainer container = RSContainer.Decode(new JagStream(raw));
            return container.GetStream().ToArray();
        }

        /// <summary>
        ///     Picks the archive ids to examine for an index, either all of them or an evenly
        ///     spread sample.
        /// </summary>
        /// <param name="table">The index's reference table.</param>
        /// <returns>The archive ids to examine, ascending.</returns>
        public IReadOnlyList<int> ArchivesToExamine(RSReferenceTable table)
        {
            var all = new List<int>(table.GetArchiveEntries().Keys);
            if (FullSweep || all.Count <= SampleArchivesPerIndex)
                return all;

            //Stride rather than prefix: a prefix would only ever exercise the low archive ids.
            int stride = all.Count / SampleArchivesPerIndex;
            var sampled = new List<int>(SampleArchivesPerIndex);
            for (int i = 0; i < all.Count; i += stride)
                sampled.Add(all[i]);
            return sampled;
        }

        /// <summary>
        ///     The XTEA key for an archive, or <c>null</c> when the loaded key file has none.
        /// </summary>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <returns>Four key words, or <c>null</c>.</returns>
        public int[] KeyFor(int indexId, int archiveId)
        {
            return _cache.GetXTEAKeyTable()?.GetKey(indexId, archiveId);
        }

        /// <summary>
        ///     Decodes a stored container, applying an XTEA key when one is held for it.
        /// </summary>
        /// <remarks>
        ///     Returns <c>null</c> rather than throwing, so callers can tell apart the archives
        ///     that cannot be read for want of a key from the ones that fail with a key in hand -
        ///     only the latter is a defect.
        /// </remarks>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <param name="stored">The captured container bytes.</param>
        /// <returns>The decoded container, or <c>null</c> when it could not be read.</returns>
        public RSContainer TryDecodeContainer(int indexId, int archiveId, byte[] stored)
        {
            int[] key = KeyFor(indexId, archiveId);

            if (key != null)
            {
                try
                {
                    return RSContainer.Decode(new JagStream(stored), key);
                }
                catch (Exception)
                {
                    //A key that does not fit means the archive was not encrypted after all -
                    //the same fallback RSCache.DecodeContainer makes.
                }
            }

            try
            {
                return RSContainer.Decode(new JagStream(stored), null);
            }
            catch (Exception)
            {
                //An undecryptable payload fails in whatever way the codec happens to notice
                //first, so the exception type carries no information worth matching on.
                return null;
            }
        }

        /// <summary>
        ///     Whether an archive can only be read by applying its XTEA key, which is what makes
        ///     it genuinely encrypted rather than merely covered by a key dump.
        /// </summary>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <param name="stored">The captured container bytes.</param>
        /// <returns><c>true</c> when the archive fails to decode without its key.</returns>
        public bool IsEncrypted(int indexId, int archiveId, byte[] stored)
        {
            try
            {
                RSContainer.Decode(new JagStream(stored), null);
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Number of six byte records the index file holds.</summary>
        /// <param name="indexId">The index id.</param>
        /// <returns>The record count.</returns>
        public int RecordCount(int indexId)
        {
            return _store.GetFileCount(indexId);
        }

        /// <summary>Reads an index record straight out of the idx file, unparsed.</summary>
        /// <param name="indexId">The index id.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <returns>The six captured bytes.</returns>
        public byte[] RawIndexRecord(int indexId, int archiveId)
        {
            JagStream stream = _store.GetIndexEntry(indexId).GetStream();
            byte[] record = new byte[RSIndex.SIZE];
            stream.Seek((long)archiveId * RSIndex.SIZE);
            stream.Read(record, 0, record.Length);
            return record;
        }

        /// <summary>
        ///     Reads an index record through <see cref="RSIndex.ReadContainerHeader"/> and writes
        ///     it back out through <see cref="RSIndex.Encode"/>.
        /// </summary>
        /// <param name="indexId">The index id.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <returns>The re-encoded six bytes.</returns>
        public byte[] ReEncodedIndexRecord(int indexId, int archiveId)
        {
            RSIndex index = _store.GetIndexEntry(indexId);
            index.ReadContainerHeader(archiveId);
            return index.Encode().ToArray();
        }

        /// <summary>Releases the cache files.</summary>
        public void Dispose()
        {
            _store?.Dispose();
        }
    }
}
