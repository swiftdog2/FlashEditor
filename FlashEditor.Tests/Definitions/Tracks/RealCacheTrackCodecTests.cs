using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlashEditor.Cache;
using FlashEditor.Definitions.Tracks;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Tracks
{
    /// <summary>
    ///     Re-encodes every packed track in indexes 6 and 11 and requires the bytes back exactly.
    /// </summary>
    /// <remarks>
    ///     This is the sweep the track path did not have. Until the decoder retained the stored form
    ///     there was nothing to re-encode: it accumulated signed byte deltas into running values and
    ///     kept only the masked MIDI, so distinct stored files projected to byte-identical output and
    ///     the deltas could not be recovered from the result. The decoder now keeps the packed runs
    ///     and the MIDI is a projection of them, which is what makes byte identity a question that
    ///     can be asked at all.
    ///     <para>
    ///     <b>Why one codec covers two indexes.</b> Not because their bytes look alike. The client
    ///     opens index 6 at InterfaceSettings.java:164 and index 11 at :168, both are parked in the
    ///     single static <c>Class269.aJS5Archive_2025</c> (Class226.java:36 for music,
    ///     Class64_Sub13.java:74 for jingles), and that static is the only argument to the only call
    ///     to the only decoder in the client - ClientScript.java:55 into
    ///     <c>Node_Sub7.method985</c>. There is no second reader that could disagree. The data half
    ///     of the claim is asserted below: the one codec accounts for every stored byte of every
    ///     declared group in both indexes, with nothing left over in front of the trailer.
    ///     </para>
    ///     <para>
    ///     The harness's padded <c>AssertExactConsumption</c> cannot be used here and its absence is
    ///     not an omission. This format's header is its <b>last</b> three bytes, so appending
    ///     sentinel padding moves the header rather than exposing an over-read, and the decode would
    ///     fail for a reason that has nothing to do with the codec. Exact consumption is asserted
    ///     instead by requiring three independently-derived figures to agree - the stored length, the
    ///     field-by-field sum of the retained spans, and what the encoder writes - which is the same
    ///     shape the reference-table trailing-byte test uses.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheTrackCodecTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>The two indexes holding this packed-MIDI format.</summary>
        /// <remarks>
        ///     Both, always. Index 11 exercises only part of the decoder - the client's channel
        ///     pressure and polyphonic key pressure arms are driven by opcode nibbles that the
        ///     jingle bank does not use - so a sweep that passed on it alone would leave two arms
        ///     and their runs untouched. The per-index nibble census is printed for that reason.
        /// </remarks>
        private static readonly int[] PackedMidiIndexes = { RSConstants.MUSIC_INDEX, RSConstants.MUSIC_2 };

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheTrackCodecTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every packed track re-encodes to the bytes the cache stores for it.
        /// </summary>
        /// <remarks>
        ///     The primary regression detector for this format, and the reason the decoder was
        ///     rewritten. Four separate fields here have more than one stored representation of the
        ///     same decoded value, so an encoder that recomputed instead of replaying would produce
        ///     a valid, playable, different file - and because the archive CRC covers the stored
        ///     bytes, that rewrites the reference-table entry of every group packed alongside it for
        ///     a track nobody edited.
        ///     <para>
        ///     Populations come from the reference table, never from a literal. Both counts differ
        ///     between builds and neither is a target; what is asserted is the relationship, that
        ///     every declared group was read, decoded and reproduced.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryTrack_ReEncodesToTheCapturedBytes()
        {
            foreach (int indexId in PackedMidiIndexes)
            {
                int declared = _fixture.DeclaredGroups(indexId);
                Assert.True(declared > 0, $"index {indexId} declares no groups, so nothing was checked");

                DefinitionSweepResult swept = Sweep(indexId).AssertReEncodesToCapturedBytes();

                //One file per group on both indexes, so a record per declared group
                Assert.Equal(declared, swept.Groups);
                Assert.Equal(declared, swept.Records);
                Assert.Equal(declared, swept.Passed);
                Assert.Equal(0, swept.Reordered);
            }
        }

        /// <summary>
        ///     One codec accounts for every stored byte of both indexes, and its own output decodes
        ///     back to itself.
        /// </summary>
        /// <remarks>
        ///     Three checks that fail in different places.
        ///     <list type="bullet">
        ///     <item><b>Exact consumption.</b> Nothing in this format states a length. The runs are
        ///     sized entirely from event counts the opcode stream implies, laid back to back, and
        ///     the only thing that says the counts were right is that the last run ends exactly
        ///     where the three-byte trailer begins. A single miscounted event shortens one run,
        ///     lengthens the next, and leaves a tail. Requiring the stored length, the sum of the
        ///     retained spans and the encoder's output to agree pins all of it.</item>
        ///     <item><b>Fixed point.</b> Independent of byte identity against the cache: this one
        ///     catches a field the encoder writes in a shape its own decoder reads differently,
        ///     which is what a save path depends on once a track has been edited. The MIDI
        ///     projection has to survive the trip too, or the round trip is stable while the
        ///     export has changed.</item>
        ///     <item><b>Shared format.</b> Both indexes go through the same codec with the same
        ///     result, which is the data-side half of the claim the client's single dispatch makes.
        ///     </item>
        ///     </list>
        ///     The meta status byte the client drops (see <c>Track.Decode</c>) cannot disturb any of
        ///     this, and that is by construction rather than by luck: it is written into the MIDI
        ///     projection only, and the encoder replays the stored opcode stream, which has no
        ///     representation of that byte at all.
        /// </remarks>
        [RealCacheFact]
        public void BothMusicIndexes_AccountForEveryStoredByteThroughOneCodec()
        {
            var swept = new List<int>();

            foreach (int indexId in PackedMidiIndexes)
            {
                int declared = _fixture.DeclaredGroups(indexId);
                Assert.True(declared > 0, $"index {indexId} declares no groups, so nothing was checked");

                var failures = new List<string>();
                var nibbles = new SortedDictionary<int, int>();
                var divisions = new SortedDictionary<int, int>();
                long storedBytes = 0;
                long runBytes = 0;
                long repairedBytes = 0;
                int withTrailingBytes = 0;

                DefinitionSweepResult result = Sweep(indexId).ForEachDecoded((record, track) =>
                {
                    byte[] stored = record.Bytes;
                    byte[] encoded = track.Encode().ToArray();

                    if (track.PackedLength != stored.Length)
                    {
                        failures.Add(Describe(indexId, record,
                            $"read {track.PackedLength} bytes of a {stored.Length}-byte file"));
                    }

                    //The field-by-field sum, derived from the retained spans rather than from
                    //where the cursor happened to land
                    if (track.StoredLength != stored.Length)
                    {
                        failures.Add(Describe(indexId, record,
                            $"its retained spans add up to {track.StoredLength} bytes, " +
                            $"the file is {stored.Length}"));
                    }

                    if (encoded.Length != stored.Length)
                    {
                        failures.Add(Describe(indexId, record,
                            $"encoded {encoded.Length} bytes from a stored {stored.Length}"));
                    }

                    if (track.TrailingBytes.Length != 0)
                    {
                        withTrailingBytes++;
                        failures.Add(Describe(indexId, record,
                            $"left {track.TrailingBytes.Length} bytes between its last run and its trailer"));
                    }

                    Track again;
                    byte[] twice;
                    try
                    {
                        again = new Track { Id = record.Id, IndexId = indexId }
                            .Decode(new JagStream(encoded));
                        twice = again.Encode().ToArray();
                    }
                    catch (Exception ex)
                    {
                        failures.Add(Describe(indexId, record,
                            $"re-decoding its own output threw {ex.GetType().Name}: {ex.Message}"));
                        return;
                    }

                    if (!twice.AsSpan().SequenceEqual(encoded))
                    {
                        failures.Add(Describe(indexId, record,
                            $"encoder output re-encoded to {twice.Length} bytes from {encoded.Length}"));
                    }

                    if (!again.Midi.AsSpan().SequenceEqual(track.Midi))
                    {
                        failures.Add(Describe(indexId, record,
                            $"the MIDI projected from its own output is {again.MidiLength} bytes " +
                            $"against the original {track.MidiLength}"));
                    }

                    storedBytes += stored.Length;
                    repairedBytes += track.RepairedMetaStatusBytes;
                    Count(divisions, track.Division);
                    foreach (TrackRun run in Track.StoredRunOrder)
                        runBytes += track.Run(run).Length;
                    foreach (byte opcode in track.Opcodes)
                        Count(nibbles, opcode & 15);
                });

                _output.WriteLine($"index {indexId}: {result.Records} of {declared} declared groups swept, " +
                                  $"{storedBytes} packed bytes, {runBytes} of them in the runs");
                _output.WriteLine($"index {indexId}: opcode low nibbles {Histogram(nibbles)}");
                _output.WriteLine($"index {indexId}: divisions {Histogram(divisions)}");
                _output.WriteLine($"index {indexId}: {repairedBytes} meta status bytes the client drops, " +
                                  "added to the projection and never to the packed form");

                Assert.Equal(declared, result.Records);
                Assert.True(failures.Count == 0, Summarise(failures));

                //The runs are meant to meet the trailer exactly. Nothing tolerates a tail here.
                Assert.Equal(0, withTrailingBytes);

                swept.Add(indexId);
            }

            Assert.Equal(PackedMidiIndexes, swept.ToArray());
        }

        /// <summary>
        ///     The music index bound to the production codec.
        /// </summary>
        /// <remarks>
        ///     Every declared group rather than the 250-group sample: "every track re-encodes to its
        ///     stored bytes" is not a claim a sample can make, and the count assertions are against
        ///     what the reference table declares.
        ///     <para>
        ///     <c>NotOpcodeTerminated</c> is not optional. A packed track's last byte is the low half
        ///     of its division field and is a zero only by coincidence, and the opcode-boundary
        ///     diagnostics it also disables cost one full decode per byte of the record - on an
        ///     index whose groups run to tens of kilobytes that turns a single failure into a hang.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">6 for music, 11 for jingles.</param>
        /// <returns>A sweep over every track the reference table declares.</returns>
        private DefinitionSweep<Track> Sweep(int indexId)
        {
            return new DefinitionSweep<Track>(_fixture, _output, indexId,
                new DefinitionCodec<Track>(LabelFor(indexId),
                    (id, stream) => new Track { Id = id, IndexId = indexId }.Decode(stream),
                    track => track.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        private static string LabelFor(int indexId)
        {
            return indexId == RSConstants.MUSIC_2 ? "jingle" : "music track";
        }

        private static string Describe(int indexId, DefinitionRecord record, string detail)
        {
            return $"index {indexId} group {record.GroupId} file {record.FileId}: {detail}";
        }

        private static void Count(SortedDictionary<int, int> counts, int value)
        {
            counts.TryGetValue(value, out int seen);
            counts[value] = seen + 1;
        }

        private static string Histogram(SortedDictionary<int, int> counts)
        {
            return string.Join(", ", counts.Select(entry => entry.Key + "=" + entry.Value));
        }

        private static string Summarise(List<string> failures)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{failures.Count} track(s) failed:");
            foreach (string failure in failures.Take(20))
                sb.AppendLine("  " + failure);
            if (failures.Count > 20)
                sb.AppendLine($"  ... and {failures.Count - 20} more");
            return sb.ToString();
        }
    }
}
