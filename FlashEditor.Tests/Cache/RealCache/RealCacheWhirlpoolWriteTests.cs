using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.IO;
using FlashEditor.Utils;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     The whirlpool recompute in <c>RSCache.WriteFile</c>, exercised on the only index in this
    ///     cache that can exercise it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Index 30 is the sole table setting the whirlpool flag, so the recompute has never had a
    ///     test of any kind: the digest primitive has unit tests, and the branch that decides when
    ///     and over what to run it has had none. A digest left stale, or taken over the wrong span,
    ///     is invisible to every sweep in this suite - the payload still round-trips, the CRC still
    ///     matches, and only a client that verifies the digest would notice.
    ///     </para>
    ///     <para>
    ///     <b>The order of the three phases is the test.</b> A no-op write is proved to write nothing
    ///     before an edit is proved to write something, because an implementation that rewrote
    ///     everything unconditionally would pass the second phase and fail the first - and the second
    ///     phase alone would be read as success. The third puts the original bytes back and requires
    ///     them to land byte for byte, which is the set-and-unset check every new edit path here has
    ///     to pass.
    ///     </para>
    ///     <para>
    ///     Against a temp copy of the cache, never the cache. Persistence is checked by <b>reopening
    ///     the store</b>: a read through the <see cref="RSCache"/> that wrote returns the new bytes
    ///     whether or not they were ever committed.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheWhirlpoolWriteTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string workingCopy;
        private readonly bool available;

        /// <summary>Takes a private copy of the cache to write into.</summary>
        /// <param name="output">The test output sink.</param>
        public RealCacheWhirlpoolWriteTests(ITestOutputHelper output)
        {
            _output = output;
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            string source = RealCacheLocator.Directory;
            if (source == null)
                return;

            workingCopy = Path.Combine(Path.GetTempPath(), "flasheditor-whirlpool-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingCopy);

            foreach (string file in Directory.GetFiles(source, "main_file_cache.*"))
                File.Copy(file, Path.Combine(workingCopy, Path.GetFileName(file)));

            //Index 30 is never encrypted, so no key file is needed here - but the copy is a whole
            //cache and other indexes in it are, and a store that cannot find keys is a different
            //shape of run. Copied into the working copy rather than left beside the source, so a
            //run against one cache cannot supply keys to a run against the other.
            string keys = XTEAKeyTable.FindKeyFile(source);
            if (keys != null)
                File.Copy(keys, Path.Combine(workingCopy, Path.GetFileName(keys)), true);

            available = true;
        }

        /// <summary>
        ///     Writing back, editing, and reverting an index-30 library, in that order.
        /// </summary>
        /// <remarks>
        ///     One test rather than three because each phase depends on the state the previous one
        ///     left, and because the working copy is a whole cache - three tests would be three
        ///     copies of it.
        /// </remarks>
        [RealCacheFact]
        public void AWrittenLibraryRecomputesItsDigestAndAnUndoneEditLandsOnTheOriginalBytes()
        {
            if (!available)
                return;

            int groupId;
            byte[] original;
            byte[] originalDigest;

            /* ---- phase one: a write that changes nothing must write nothing ---------------- */
            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                RSReferenceTable table = cache.GetReferenceTable(RSConstants.NATIVE_LIBRARIES);

                Assert.True(table.usesWhirlpool,
                    "index 30 does not carry whirlpool digests here, so this test proves nothing");

                //The smallest group, so the phases below re-compress as little as possible. Chosen
                //by measurement rather than by id: the two caches agree on this index, but a
                //hardcoded id would be a claim about layout that nothing here needs to make.
                groupId = table.GetArchiveEntries().Keys
                    .OrderBy(id => cache.ReadFileBytes(RSConstants.NATIVE_LIBRARIES, id,
                        table.GetArchiveEntry(id).GetValidFileIds().Single()).Length)
                    .First();

                int fileId = table.GetArchiveEntry(groupId).GetValidFileIds().Single();
                original = cache.ReadFileBytes(RSConstants.NATIVE_LIBRARIES, groupId, fileId);
                originalDigest = (byte[]) table.GetArchiveEntry(groupId).GetWhirlpool().Clone();

                _output.WriteLine($"index 30 group {groupId}: {original.Length:N0} byte payload");

                Assert.False(cache.HasUnsavedChanges, "reading staged a change");

                cache.WriteFile(RSConstants.NATIVE_LIBRARIES, groupId, fileId, new JagStream(original));

                //Nothing staged: the payload is identical, so the stored bytes, the CRC, the version
                //and the digest all still describe what is on disk and must be left exactly as they
                //are. Re-storing them would rewrite this archive and drag in the reference-table
                //entry of every archive packed beside it.
                Assert.False(cache.HasUnsavedChanges, "a no-op write staged a change");
                Assert.Equal(originalDigest, table.GetArchiveEntry(groupId).GetWhirlpool());
            }

            /* ---- phase two: a real edit recomputes the digest over the new container -------- */
            byte[] edited = (byte[]) original.Clone();
            edited[edited.Length / 2] ^= 0xFF;

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                int fileId = cache.GetReferenceTable(RSConstants.NATIVE_LIBRARIES)
                    .GetArchiveEntry(groupId).GetValidFileIds().Single();

                cache.WriteFile(RSConstants.NATIVE_LIBRARIES, groupId, fileId, new JagStream(edited));
                Assert.True(cache.HasUnsavedChanges, "an edited payload staged nothing");

                cache.WriteCache(workingCopy);
                Assert.False(cache.HasUnsavedChanges);
            }

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                RSReferenceTable table = cache.GetReferenceTable(RSConstants.NATIVE_LIBRARIES);
                RSArchiveEntry entry = table.GetArchiveEntry(groupId);
                int fileId = entry.GetValidFileIds().Single();

                Assert.Equal(edited, cache.ReadFileBytes(RSConstants.NATIVE_LIBRARIES, groupId, fileId));

                AssertDigestDescribesTheStoredContainer(cache, entry, groupId, "after the edit");

                //And it is a different digest. Without this the assertion above is satisfied by a
                //recompute that never ran, since a stale digest over unchanged bytes would also
                //match - which is precisely the defect this test exists to catch.
                Assert.NotEqual(originalDigest, entry.GetWhirlpool());
            }

            /* ---- phase three: undo the edit and land on the original bytes ----------------- */
            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                int fileId = cache.GetReferenceTable(RSConstants.NATIVE_LIBRARIES)
                    .GetArchiveEntry(groupId).GetValidFileIds().Single();

                cache.WriteFile(RSConstants.NATIVE_LIBRARIES, groupId, fileId, new JagStream(original));
                Assert.True(cache.HasUnsavedChanges, "restoring the original payload staged nothing");

                cache.WriteCache(workingCopy);
            }

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                RSReferenceTable table = cache.GetReferenceTable(RSConstants.NATIVE_LIBRARIES);
                RSArchiveEntry entry = table.GetArchiveEntry(groupId);
                int fileId = entry.GetValidFileIds().Single();

                //The payload is what it started as, to the byte. The digest is NOT what it started
                //as, and must not be asserted to be: a GZip re-encode is never byte-identical, so
                //the container holding these bytes is a different, equally valid encoding of them
                //and its hash legitimately differs. What has to hold is that the digest describes
                //the container that is actually there.
                Assert.Equal(original, cache.ReadFileBytes(RSConstants.NATIVE_LIBRARIES, groupId, fileId));
                AssertDigestDescribesTheStoredContainer(cache, entry, groupId, "after the edit was undone");
            }
        }

        /// <summary>
        ///     Asserts a group's digest and CRC both cover the stored container minus its trailer.
        /// </summary>
        /// <remarks>
        ///     Both, because they are recomputed over one span in <c>RSCache.WriteFile</c> and
        ///     checking only the digest would leave a span error visible on the half the shipped
        ///     cache already pins.
        /// </remarks>
        /// <param name="cache">The reopened cache.</param>
        /// <param name="entry">The group's reference-table entry.</param>
        /// <param name="groupId">The group id, for the failure message.</param>
        /// <param name="phase">Which phase of the test is asserting, for the failure message.</param>
        private void AssertDigestDescribesTheStoredContainer(RSCache cache, RSArchiveEntry entry,
            int groupId, string phase)
        {
            byte[] stored = cache.LoadContainer(RSConstants.NATIVE_LIBRARIES, groupId).ToArray();
            byte[] hashed = stored.AsSpan(0, stored.Length - 2).ToArray();

            _output.WriteLine($"group {groupId} {phase}: {stored.Length:N0} stored bytes");

            Assert.Equal(Whirlpool.ComputeHash(hashed), entry.GetWhirlpool());
            Assert.Equal((int) CRC32Helper.ComputeCrc32(hashed), entry.GetCrc());

            //The trailer really is outside the span, rather than the two figures agreeing because
            //the trailer happens to be absent.
            Assert.NotEqual(Whirlpool.ComputeHash(stored), entry.GetWhirlpool());
        }

        /// <summary>Removes the working copy.</summary>
        public void Dispose()
        {
            if (workingCopy == null || !Directory.Exists(workingCopy))
                return;

            try
            {
                Directory.Delete(workingCopy, true);
            }
            catch (IOException)
            {
                //A leftover temp copy is untidy, not a failure.
            }
        }
    }
}
