using FlashEditor.Cache;
using FlashEditor.Definitions.Tracks;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Tracks
{
    /// <summary>
    ///     Sweeps every packed music track in the shipped cache and checks the decoder against the
    ///     format's own accounting rather than against itself.
    /// </summary>
    /// <remarks>
    ///     These checks are about the MIDI <b>projection</b>, and they stay worth running now that
    ///     the byte-identity sweep exists next door in <see cref="RealCacheTrackCodecTests"/> -
    ///     because that sweep cannot see them. The encoder replays the stored runs verbatim, so it
    ///     would reproduce the cache byte for byte even if the projection were nonsense.
    ///     <para>
    ///     Two independent checks cover it. The packed file states how long the decoded MIDI must
    ///     be - the client sizes its output buffer from the first pass and never re-measures
    ///     (Node_Sub7.java:166) - so the emitted length has to reconcile with it. And the emitted
    ///     file has to be a structurally valid MIDI: an MThd whose track count matches the number of
    ///     MTrk chunks, chunk lengths that tile the file exactly, and an end-of-track meta event
    ///     closing every chunk. Neither can be satisfied by a decoder that has the run boundaries
    ///     wrong, which is the failure mode this format invites.
    ///     </para>
    /// </remarks>
    public sealed class RealCacheTrackTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Music. 963 groups in the shipped 639 cache, one file each.</summary>
        private const int MusicIndex = RSConstants.MUSIC_INDEX;

        /// <summary>Jingles, the same packed format. 441 groups in the shipped 639 cache.</summary>
        private const int JingleIndex = RSConstants.MUSIC_2;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        public RealCacheTrackTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [RealCacheFact]
        public void EveryTrackDecodesToAStructurallyValidMidi()
        {
            RSCache cache = _fixture.OpenCache();
            var failures = new List<string>();
            int decoded = 0;
            int repairedTracks = 0;
            long repairedBytes = 0;

            foreach (int indexId in new[] { MusicIndex, JingleIndex })
            {
                RSReferenceTable table = cache.GetReferenceTable(indexId);

                foreach (int groupId in table.GetArchiveEntries().Keys)
                {
                    Track track;
                    try
                    {
                        track = cache.GetTrack(indexId, groupId);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"index {indexId} group {groupId}: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    decoded++;
                    if (track.RepairedMetaStatusBytes > 0)
                    {
                        repairedTracks++;
                        repairedBytes += track.RepairedMetaStatusBytes;
                    }

                    //The packed file's own size accounting, plus the meta status bytes the client
                    //would have dropped. See the CLIENT BUG note on Track.Decode.
                    int expected = track.ExpectedMidiLength + track.RepairedMetaStatusBytes;
                    if (track.MidiLength != expected)
                        failures.Add($"index {indexId} group {groupId}: emitted {track.MidiLength} bytes, "
                            + $"packed file predicts {expected}");

                    string structural = DescribeStructuralFault(track);
                    if (structural != null)
                        failures.Add($"index {indexId} group {groupId}: {structural}");
                }
            }

            _output.WriteLine($"decoded {decoded} tracks; {repairedTracks} needed a meta status byte "
                + $"the client omits ({repairedBytes} bytes in total)");

            Assert.True(failures.Count == 0, Summarise(failures));
        }

        /// <summary>
        ///     Pins the music names to the tracks by the hash the archive stores, and pins the
        ///     rejected join alongside it.
        /// </summary>
        /// <remarks>
        ///     Every name this produces is self-proving: it is attached to a group only because
        ///     hashing it reproduces the group's stored identifier, and the assertion below rounds
        ///     that trip through the reference table's own name lookup rather than through the map
        ///     under test. The one track whose name is known without any of this is "scape main",
        ///     which the client asks index 6 for by name (InterfaceSettings.java:216).
        ///
        ///     The second half of the test exists because the obvious join is wrong and looks
        ///     right. Keying the enum by group id covers 958 of 963 groups and 958 of its 970 keys,
        ///     which reads as confirmation, and it names group 0 "Adventure" when that group's
        ///     stored hash proves it is "Scape Main". The enum key is the music player's list
        ///     position, and its values are in alphabetical order. Asserting that the two joins
        ///     disagree keeps a future reader from "simplifying" back to the broken one.
        /// </remarks>
        [RealCacheFact]
        public void TrackNamesJoinOnTheArchiveNameHash()
        {
            RSCache cache = _fixture.OpenCache();
            Dictionary<int, string> names = TrackNames.Load(cache);

            Assert.NotEmpty(names);

            RSReferenceTable music = cache.GetReferenceTable(MusicIndex);
            SortedDictionary<int, RSArchiveEntry> groups = music.GetArchiveEntries();

            int named = 0;
            var wrong = new List<string>();

            foreach (KeyValuePair<int, RSArchiveEntry> group in groups)
            {
                if (!names.TryGetValue(group.Value.GetIdentifier(), out string name))
                    continue;

                named++;

                //Independent of the map under test: ask the table which group carries that name
                int resolved = music.GetArchiveId(name);
                if (resolved != group.Key)
                    wrong.Add($"group {group.Key} named \"{name}\", which the table resolves to {resolved}");
            }

            _output.WriteLine($"{names.Count} distinct names, {named} of {groups.Count} groups named");

            Assert.True(wrong.Count == 0, Summarise(wrong));

            //Loose bound on purpose: this asserts that the join works at all, not a count a
            //different cache revision would have to match.
            Assert.True(named > groups.Count / 2,
                $"only {named} of {groups.Count} index-{MusicIndex} groups could be named");

            //The one track whose name is known without the name list
            int scapeMain = music.GetArchiveId("scape main");
            Assert.True(scapeMain >= 0, "index 6 has no group named \"scape main\"");
            Assert.Equal("Scape Main", names[groups[scapeMain].GetIdentifier()]);

            //Index 11 carries no identifiers, so no jingle can pick up a name
            foreach (KeyValuePair<int, RSArchiveEntry> jingle in cache.GetReferenceTable(JingleIndex).GetArchiveEntries())
                Assert.Equal(-1, jingle.Value.GetIdentifier());
        }

        /// <summary>
        ///     Checks the emitted file against the MIDI chunk structure, independently of anything
        ///     the decoder recorded about itself.
        /// </summary>
        /// <param name="track">The decoded track.</param>
        /// <returns>A description of the first fault, or <c>null</c> when the file is well formed.</returns>
        private static string DescribeStructuralFault(Track track)
        {
            byte[] midi = track.Midi;

            if (midi == null || midi.Length < 14)
                return "no MThd header";
            if (Tag(midi, 0) != "MThd")
                return "first chunk is not MThd";
            if (ReadInt(midi, 4) != 6)
                return "MThd length is not 6";

            int format = ReadShort(midi, 8);
            int declaredTracks = ReadShort(midi, 10);
            int division = ReadShort(midi, 12);

            if (format != (track.TrackCount > 1 ? 1 : 0))
                return $"MThd format {format} does not match {track.TrackCount} tracks";
            if (declaredTracks != track.TrackCount)
                return $"MThd declares {declaredTracks} tracks, header said {track.TrackCount}";
            if (division != track.Division)
                return $"MThd division {division} does not match {track.Division}";

            int position = 14;
            int chunks = 0;

            while (position < midi.Length)
            {
                if (position + 8 > midi.Length)
                    return $"chunk header runs off the end at {position}";
                if (Tag(midi, position) != "MTrk")
                    return $"chunk at {position} is not MTrk";

                int length = ReadInt(midi, position + 4);
                if (length < 0 || position + 8 + length > midi.Length)
                    return $"MTrk at {position} declares {length} bytes, past the end of the file";

                int end = position + 8 + length;

                //Every track must be closed by an end-of-track meta event
                if (length < 3 || midi[end - 3] != 0xFF || midi[end - 2] != 0x2F || midi[end - 1] != 0x00)
                    return $"MTrk at {position} does not end with FF 2F 00";

                position = end;
                chunks++;
            }

            if (position != midi.Length)
                return $"chunks stop at {position} of {midi.Length}";
            if (chunks != track.TrackCount)
                return $"{chunks} MTrk chunks for {track.TrackCount} declared tracks";

            return null;
        }

        private static string Tag(byte[] data, int offset)
        {
            return Encoding.ASCII.GetString(data, offset, 4);
        }

        private static int ReadInt(byte[] data, int offset)
        {
            return data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3];
        }

        private static int ReadShort(byte[] data, int offset)
        {
            return data[offset] << 8 | data[offset + 1];
        }

        private static string Summarise(List<string> failures)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{failures.Count} track(s) failed:");
            for (int i = 0; i < failures.Count && i < 20; i++)
                sb.AppendLine("  " + failures[i]);
            if (failures.Count > 20)
                sb.AppendLine($"  ... and {failures.Count - 20} more");
            return sb.ToString();
        }
    }
}
