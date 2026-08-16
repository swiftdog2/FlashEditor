using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Config;
using FlashEditor.IO;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Config {
    /// <summary>
    ///     Editing a field of a real index 2 record, which no sweep in this project covered.
    /// </summary>
    /// <remarks>
    ///     <b>The byte-identity sweeps prove an unedited record re-encodes to what it was read
    ///     from.</b> That is a different claim from the one here, and four real defects in this
    ///     repository have lived in the gap between them. Index 2 opened thirty-five families to
    ///     editing at once, so the gap it opens is the widest yet.
    ///     <para>
    ///     The assertion is the one an asymmetric setter cannot pass: set a field to something else,
    ///     set it back, and land on the <b>original stored bytes</b>. Compared against what the cache
    ///     holds rather than against a re-encode taken before the edit, because a re-encode compared
    ///     with itself agrees with a setter that moved an opcode both times - which is exactly how
    ///     the object and NPC flag setters failed.
    ///     </para>
    ///     <para>
    ///     Ordering is what makes this index the hard case. Not one of group 36's 1,051 files is in
    ///     ascending opcode order, group 46 has none of its 28, group 31 none of its 4, and group 32
    ///     spreads 1,972 files over 58 distinct orders. A setter that rebuilt the opcode stream from
    ///     the values would pass a naive round trip and fail this on almost every record.
    ///     </para>
    ///     <para>
    ///     <b>There is no <c>or</c> in the assertion.</b> The one field that legitimately cannot be
    ///     undone is excluded by name and by the condition that makes it so, rather than being
    ///     allowed to pass - see <see cref="IsExempt"/>.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheConfigFieldEditTests : IClassFixture<RealCacheFixture> {
        /// <summary>
        ///     How many records of one group are exercised outside a full sweep.
        /// </summary>
        /// <remarks>
        ///     Every editable field of every sampled record is set and unset, so the work is records
        ///     times fields times two encodes - and group 32 alone carries 1,972 records of about
        ///     twenty-five editable fields each. <c>FLASHEDITOR_TEST_CACHE_FULL=1</c> lifts the cap,
        ///     which is where the whole index is covered.
        /// </remarks>
        private const int SampledRecordsPerGroup = 120;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheConfigFieldEditTests(RealCacheFixture cache, ITestOutputHelper output) {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Every editable field of every modelled family, set and set back, lands on the stored
        ///     bytes.
        /// </summary>
        [RealCacheFact]
        public void EveryEditableFieldSetAndSetBackLandsOnTheStoredBytes() {
            RSCache cache = _cache.OpenCache();
            var failures = new List<string>();
            int fieldsChecked = 0;
            int recordsChecked = 0;

            foreach (ConfigFamily family in ConfigFamily.Modelled) {
                if (!family.CanEncode)
                    continue;

                foreach (int fileId in Sample(cache, family.GroupId)) {
                    byte[] stored = cache.ReadFileBytes(RSConstants.CONFIG, family.GroupId, fileId);
                    ConfigRecord record = family.Read(fileId, new JagStream(stored));

                    if (record.Definition == null)
                        continue;

                    recordsChecked++;

                    foreach (ConfigField field in record.Fields) {
                        if (!field.IsEditable || IsExempt(family, record, field))
                            continue;

                        fieldsChecked++;

                        string original = field.Value;
                        string probe = Probe(field);

                        try {
                            field.Write(probe!);
                            field.Write(original);
                        }
                        catch (Exception ex) {
                            failures.Add(Where(family, fileId, field) + " threw on \"" + probe +
                                "\": " + ex.Message);
                            continue;
                        }

                        byte[] back = family.Encode(record.Definition).ToArray();

                        if (!back.AsSpan().SequenceEqual(stored))
                            failures.Add(Where(family, fileId, field) + " set to \"" + probe +
                                "\" and back to \"" + original + "\" produced " + back.Length +
                                " bytes where the cache stores " + stored.Length + ": " +
                                Hex(stored) + " became " + Hex(back));
                    }
                }
            }

            _output.WriteLine("Checked " + fieldsChecked.ToString("N0") + " editable fields over " +
                recordsChecked.ToString("N0") + " index 2 records" +
                (_cache.FullSweep ? " (full sweep)" : " (sampled)"));

            Assert.Empty(failures);
        }

        /// <summary>
        ///     Setting a field to something different actually changes the stored bytes.
        /// </summary>
        /// <remarks>
        ///     <b>The half the round trip above cannot see.</b> A setter that did nothing at all
        ///     passes it perfectly, and that is not hypothetical here: assigning
        ///     <c>MapSceneIconDefinition.SpriteGroupId</c> on one of the seven records carrying
        ///     opcode 4 leaves the opcode in the stream, so the file re-encodes identically and the
        ///     edit vanishes with no error anywhere. Every editable field has to be able to move at
        ///     least one byte.
        ///     <para>
        ///     Fields whose probe cannot differ are excluded rather than tolerated: a scaled field
        ///     can legitimately absorb a probe one unit away, and a field the record does not carry
        ///     may re-encode to the same bytes because its probe equals the constructor default.
        ///     Both are decided from the field before the assertion rather than by letting a failure
        ///     pass.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryEditableFieldCanChangeTheStoredBytes() {
            RSCache cache = _cache.OpenCache();
            var inert = new List<string>();
            int moved = 0;

            foreach (ConfigFamily family in ConfigFamily.Modelled) {
                if (!family.CanEncode)
                    continue;

                foreach (int fileId in Sample(cache, family.GroupId)) {
                    byte[] stored = cache.ReadFileBytes(RSConstants.CONFIG, family.GroupId, fileId);
                    ConfigRecord record = family.Read(fileId, new JagStream(stored));

                    if (record.Definition == null)
                        continue;

                    foreach (ConfigField field in record.Fields) {
                        if (!field.IsEditable || IsExempt(family, record, field))
                            continue;

                        string original = field.Value;
                        string probe = Probe(field);

                        //A probe that renders back to the original cannot say anything, so it is
                        //not asked to.
                        if (probe == original)
                            continue;

                        byte[] probed;
                        try {
                            field.Write(probe);
                            probed = family.Encode(record.Definition).ToArray();
                            field.Write(original);
                        }
                        catch (Exception) {
                            //Reported by the round-trip test above rather than twice.
                            continue;
                        }

                        if (probed.AsSpan().SequenceEqual(stored))
                            inert.Add(Where(family, fileId, field) + " set to \"" + probe +
                                "\" re-encoded to the bytes already stored, so the edit does nothing");
                        else
                            moved++;
                    }
                }
            }

            _output.WriteLine(moved.ToString("N0") + " field edits changed the stored bytes");
            Assert.Empty(inert);
        }

        /// <summary>
        ///     Group 34's two encodings of "no icon" survive an edit to the sprite and back.
        /// </summary>
        /// <remarks>
        ///     Called out on its own because it is the case that breaks the naive setter, and
        ///     because the two forms are indistinguishable from the decoded value - both leave the
        ///     sprite at -1. Measured: seven of the hundred records carry opcode 4 and 93 carry
        ///     opcode 1, in both caches, so both branches are exercised by real data.
        ///     <para>
        ///     The assertion is on the opcode stream as well as the bytes. Identical bytes already
        ///     imply an identical stream, but naming the stream is what makes a failure say which of
        ///     the two encodings was lost rather than only that the file changed.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryMapSceneIconKeepsItsNoIconEncodingAcrossAnEdit() {
            RSCache cache = _cache.OpenCache();
            var failures = new List<string>();
            int explicitNone = 0;
            int named = 0;

            foreach (int fileId in cache.GetFileIds(RSConstants.CONFIG, ConfigGroup.MapSceneIcon)) {
                byte[] stored = cache.ReadFileBytes(RSConstants.CONFIG, ConfigGroup.MapSceneIcon, fileId);

                var record = new MapSceneIconDefinition { Id = fileId };
                record.Decode(new JagStream(stored));

                string before = record.DescribeAbsentIconEncoding();
                int original = record.SpriteGroupId;

                if (record.DecodedOpcodes.Any(entry => entry.Opcode == 4))
                    explicitNone++;
                if (record.DecodedOpcodes.Any(entry => entry.Opcode == 1))
                    named++;

                //Away from whatever the record holds, in both directions: a record naming an icon is
                //cleared, and one naming none is given an icon.
                record.SetSpriteGroupId(original < 0 ? 7 : -1);
                record.SetSpriteGroupId(original);

                byte[] back = record.Encode().ToArray();

                if (!back.AsSpan().SequenceEqual(stored))
                    failures.Add("Map scene icon " + fileId + " (" + before + ") did not come back: " +
                        Hex(stored) + " became " + Hex(back));
                else if (record.DescribeAbsentIconEncoding() != before)
                    failures.Add("Map scene icon " + fileId + " changed encoding from \"" + before +
                        "\" to \"" + record.DescribeAbsentIconEncoding() + "\"");
            }

            _output.WriteLine(named + " records name an icon through opcode 1, " + explicitNone +
                " store the explicit none of opcode 4");

            //Both branches have to be exercised, or the sweep is only testing one of them and would
            //keep passing if the other were broken.
            Assert.True(named > 0, "No group 34 record names an icon, so opcode 1 is untested");
            Assert.True(explicitNone > 0,
                "No group 34 record stores opcode 4, so the encoding this test exists for is untested");
            Assert.Empty(failures);
        }

        /// <summary>
        ///     Every quest the inverted item join names is a record group 35 declares.
        /// </summary>
        /// <remarks>
        ///     The forward relation is measured - <c>item opcode 132 -> config group 35</c> - and
        ///     this is the only claim the inversion adds: that it invents no target. A count would
        ///     not do, because item definitions differ between the two caches (20,427 against the
        ///     repack's 20,470), so the assertion is the relationship rather than a number.
        /// </remarks>
        [RealCacheFact]
        public void EveryQuestTheItemJoinNamesIsDeclaredByGroup35() {
            RSCache cache = _cache.OpenCache();
            QuestItemIndex index = QuestItemIndex.Build(cache);

            int[] declared = cache.GetFileIds(RSConstants.CONFIG, ConfigGroup.Quest);
            var known = new HashSet<int>(declared);
            var named = new List<int>();

            foreach (int quest in declared) {
                IReadOnlyList<int> items = index.ItemsNaming(quest);
                if (items.Count > 0)
                    named.Add(quest);

                foreach (int item in items)
                    Assert.True(known.Contains(quest),
                        "Item " + item + " names quest " + quest + ", which group 35 does not declare");
            }

            _output.WriteLine("Read " + index.ItemsRead.ToString("N0") + " item definitions, " +
                index.ItemsFailed + " failed; " + named.Count + " of " + declared.Length +
                " quests are named by at least one item");

            Assert.Equal(0, index.ItemsFailed);
            Assert.NotEmpty(named);
        }

        /// <summary>
        ///     The one field that cannot be undone, and the condition that makes it so.
        /// </summary>
        /// <remarks>
        ///     <b>A floor overlay distinguishes "stores no colour" from "stores black"</b>, because
        ///     they are different bytes: opcode 1 absent against opcode 1 carrying <c>0x000000</c>.
        ///     Setting the colour on a record that stores none therefore has to set
        ///     <c>HasPrimaryRgb</c> as well, or the codec's <c>AddedOpcodes</c> has no signal and the
        ///     edit writes nothing at all - and once set, no value of that same field can put it
        ///     back, because the field is the colour and not its presence.
        ///     <para>
        ///     Stated as one named field under one named condition rather than as a family-wide
        ///     exemption. A record that <i>does</i> carry opcode 1 is checked like everything else,
        ///     which is 233 of the 235 records in both caches - so this excludes two records of one
        ///     field, not a family.
        ///     </para>
        /// </remarks>
        /// <param name="family">The family being swept.</param>
        /// <param name="record">The decoded record.</param>
        /// <param name="field">The field.</param>
        /// <returns>Whether the field is excluded on this record.</returns>
        private static bool IsExempt(ConfigFamily family, ConfigRecord record, ConfigField field) {
            if (family.GroupId != ConfigGroup.FloorOverlay || field.Name != "Primary colour")
                return false;

            return record.Definition is FloorOverlayDefinition overlay && !overlay.HasPrimaryRgb;
        }

        /// <summary>
        ///     A value different from the one the field holds, in the field's own notation.
        /// </summary>
        /// <remarks>
        ///     Written in the notation the cell renders so it goes back through the same parser the
        ///     user's typing does. A probe that bypassed the parser would test the setter and leave
        ///     the half of the path a user actually reaches untested.
        /// </remarks>
        /// <param name="field">The field.</param>
        /// <returns>The probe.</returns>
        private static string Probe(ConfigField field) {
            switch (field.Editor) {
                case ConfigFieldEditor.Flag:
                    return field.Value == "true" ? "false" : "true";

                case ConfigFieldEditor.Text:
                    //Appended rather than replaced, so a field with a length rule sees a value of a
                    //length it already tolerates plus one.
                    return field.Value + "x";

                case ConfigFieldEditor.Colour:
                    return field.Value == "none"
                        ? "0x123456"
                        : "0x" + (ReadHex(field.Value) ^ 0x00FF00).ToString("X6", CultureInfo.InvariantCulture);

                default:
                    /* Integer and the four id kinds all render as a decimal integer, and all four
                       ids are picked as one on the field pane.

                       Four rather than one, because three fields in this index are stored scaled by
                       four - a floor's texture scale is decoded as a short shifted left two, and a
                       floor overlay's water depth is written as a byte shifted right two. A probe
                       one unit away is absorbed by that shift and re-encodes to the byte already
                       stored, which would read as a field that cannot be edited when it is only a
                       probe too small to survive the encoding. */
                    return (ReadInt(field.Value) + 4).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The records of one group this run exercises.</summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="groupId">The group.</param>
        /// <returns>The file ids, table-driven and never a 0..count walk.</returns>
        private IEnumerable<int> Sample(RSCache cache, int groupId) {
            //Table-driven: eight of index 2's groups have holes in the middle of their id range, so
            //a count would ask for records that do not exist and stop short of the ones that do.
            int[] ids = cache.GetFileIds(RSConstants.CONFIG, groupId);

            if (_cache.FullSweep || ids.Length <= SampledRecordsPerGroup)
                return ids;

            //Spread across the id space rather than the first N. The interesting records in this
            //index are not clustered at the start - group 4's doubled opcode is file 94 and group
            //36's are 779 and 780.
            int stride = ids.Length / SampledRecordsPerGroup;
            return ids.Where((_, position) => position % stride == 0);
        }

        private static string Where(ConfigFamily family, int fileId, ConfigField field) {
            return "Group " + family.GroupId + " (" + family.Name + ") " + family.RowNoun + " " +
                fileId + " field \"" + field.Name + "\"";
        }

        private static int ReadInt(string text) {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
        }

        private static int ReadHex(string text) {
            string trimmed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? text.Substring(2)
                : text;

            return int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out int value) ? value : 0;
        }

        /// <summary>A record's bytes in hex, truncated so one failure line stays readable.</summary>
        private static string Hex(byte[] bytes) {
            const int Shown = 48;
            string hex = BitConverter.ToString(bytes, 0, Math.Min(Shown, bytes.Length)).Replace('-', ' ');
            return bytes.Length <= Shown ? hex : hex + " ... (" + bytes.Length + " bytes)";
        }
    }
}
