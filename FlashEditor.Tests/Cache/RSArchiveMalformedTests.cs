using System;
using System.IO;
using System.Linq;
using FlashEditor.cache;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     A malformed archive must fail as a thrown error, never by taking the process with it.
    /// </summary>
    /// <remarks>
    ///     <see cref="RSArchive.Decode"/> reads its size table from a fixed offset back from the end,
    ///     computed from the chunk count and the file count. Give it the wrong file count and it
    ///     reads that table out of the middle of the payload, so the chunk lengths are arbitrary
    ///     ints.
    ///
    ///     The dangerous case is not a negative length or an absurd one that throws. It is a length
    ///     of a few hundred megabytes on a machine with memory free, because then
    ///     <c>new byte[chunkSize]</c> SUCCEEDS. Nothing raises, the caller's <c>catch</c> never
    ///     runs, and a loop over candidate file counts allocates until the OS kills the process.
    ///     That is what a corrupt archive would do to the editor, and it is uncatchable by
    ///     construction, so it has to be prevented rather than handled.
    ///
    ///     The bound is free: chunks are read sequentially out of the payload, so their running
    ///     total cannot exceed the payload's own length.
    /// </remarks>
    public sealed class RSArchiveMalformedTests
    {
        /// <summary>
        ///     Two files, one chunk, with caller-chosen size-table deltas.
        /// </summary>
        /// <remarks>
        ///     Two files rather than one deliberately: <see cref="RSArchive.Decode"/> returns early
        ///     for a single-file archive and treats the whole payload as the file, never reading a
        ///     size table at all. So a one-file case cannot reach the code under test, which is also
        ///     why the real crash only appeared while probing multi-file counts.
        /// </remarks>
        private static JagStream TwoFileArchive(int firstDelta, int secondDelta, int bodyLength = 32)
        {
            var stream = new JagStream();
            stream.Write(Enumerable.Range(0, bodyLength).Select(i => (byte) i).ToArray());
            stream.WriteInteger(firstDelta);
            stream.WriteInteger(secondDelta);
            stream.WriteByte(1);                //one chunk
            stream.Flip();
            return stream;
        }

        /// <summary>
        ///     A size table read at the wrong offset throws rather than allocating what it asks for.
        /// </summary>
        /// <remarks>
        ///     A gigabyte is the point: it is small enough that the runtime allocates it happily on
        ///     a machine with memory free, so it never raised and never could be caught.
        /// </remarks>
        [Fact]
        public void AnArchiveWhoseSizeTableDeclaresMoreThanItHolds_Throws()
        {
            Assert.Throws<InvalidDataException>(
                () => RSArchive.Decode(TwoFileArchive(0x40000000, 0), new[] { 0, 1 }));
        }

        /// <summary>
        ///     A negative running total is rejected on the same path.
        /// </summary>
        /// <remarks>
        ///     The deltas are signed and cumulative, so a table read at the wrong offset can drive
        ///     the total below zero as easily as above the payload length.
        /// </remarks>
        [Fact]
        public void AnArchiveWhoseSizeTableGoesNegative_Throws()
        {
            Assert.Throws<InvalidDataException>(
                () => RSArchive.Decode(TwoFileArchive(-4096, 0), new[] { 0, 1 }));
        }

        /// <summary>
        ///     A well-formed two-file archive still decodes, so the guard is not refusing everything.
        /// </summary>
        [Fact]
        public void AWellFormedArchive_StillDecodes()
        {
            //Deltas are cumulative per chunk: file 0 takes 20 bytes, file 1 the remaining 12.
            RSArchive archive = RSArchive.Decode(TwoFileArchive(20, -8), new[] { 0, 1 });

            Assert.Equal(Enumerable.Range(0, 20).Select(i => (byte) i), archive.GetFile(0).ToArray());
            Assert.Equal(Enumerable.Range(20, 12).Select(i => (byte) i), archive.GetFile(1).ToArray());
        }
    }
}
