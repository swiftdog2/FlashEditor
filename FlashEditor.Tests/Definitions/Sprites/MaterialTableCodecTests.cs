using System;
using FlashEditor;
using FlashEditor.Definitions.Sprites;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     Synthetic pins for the index-26 write path: which bytes an edit is allowed to move.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Everything here is built rather than captured, and deliberately so. The layout itself is
    ///     pinned by <c>RealCacheMaterialTests</c> against the shipped file, which is the only thing
    ///     that can settle it; what these tests cover is the part no sweep over either cache can
    ///     reach. Three of the nineteen columns decode many-to-one - the boolean columns collapse
    ///     every byte outside {0,1}, and the existence column collapses everything that is not 1 -
    ///     and a per-column profile of the repack's records found no instance of any of them. An
    ///     encoder that rebuilt those columns from their fields would therefore sweep perfectly
    ///     clean and corrupt the file the first time a cache did hold one.
    ///     </para>
    ///     <para>
    ///     In the "RealCache" collection because two of these drive <c>TextureManager</c>'s static
    ///     dictionary through <c>Clear</c>, which disposes definitions
    ///     <c>TextureGraphConformanceTests</c> and <c>RealCacheMapIconTests</c> are reading.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public class MaterialTableCodecTests
    {
        /// <summary>A boolean byte no cache holds, which decodes to false and cannot be rebuilt.</summary>
        private const byte AliasedBooleanByte = MaterialFileBuilder.AliasedBooleanByte;

        /// <summary>An existence byte the client reads as an empty slot, being anything but 1.</summary>
        private const byte AliasedAbsentByte = MaterialFileBuilder.AliasedAbsentByte;

        /// <summary>A record whose every byte is distinct, so a misplaced column is visible.</summary>
        /// <param name="seed">Value of the record's first byte.</param>
        /// <returns>The 23 stored bytes.</returns>
        private static byte[] Row(int seed) => MaterialFileBuilder.Row(seed);

        /// <summary>Interleaves whole records into the column-major file the client reads.</summary>
        /// <param name="existence">One existence byte per slot.</param>
        /// <param name="rows">One 23-byte record per slot, ignored where the slot is absent.</param>
        /// <returns>The encoded file.</returns>
        private static byte[] BuildFile(byte[] existence, byte[][] rows) =>
            MaterialFileBuilder.BuildFile(existence, rows);

        /// <summary>A two-slot file whose first record carries a boolean byte no cache holds.</summary>
        /// <returns>The existence column, the rows and the encoded file.</returns>
        private static (byte[] Existence, byte[][] Rows, byte[] File) FileWithAnAliasedBoolean() =>
            MaterialFileBuilder.FileWithAnAliasedBoolean();

        /// <summary>A table nobody has touched re-encodes to the file it was read from.</summary>
        [Fact]
        public void AnUntouchedTable_ReEncodesToItsStoredBytes()
        {
            (_, _, byte[] file) = FileWithAnAliasedBoolean();

            MaterialTable table = MaterialTable.Decode(new JagStream(file));

            Assert.False(table.IsDirty);
            Assert.Equal(file, table.Encode().ToArray());
        }

        /// <summary>
        ///     Editing one column leaves every other byte of the same record exactly as stored.
        /// </summary>
        /// <remarks>
        ///     The reason the dirty flag is per column rather than per record. Record 0 carries a
        ///     boolean byte of 2, which decodes to false and would come back as 0 from the field;
        ///     re-encoding a whole record because one of its nineteen columns changed would lose it.
        /// </remarks>
        [Fact]
        public void EditingOneColumn_LeavesTheOtherColumnsOfThatRecordAsStored()
        {
            (_, _, byte[] file) = FileWithAnAliasedBoolean();
            MaterialTable table = MaterialTable.Decode(new JagStream(file));

            TextureDefinition edited = table.Slots[0];
            Assert.False(edited.force64x64, "a boolean byte of 2 is false to the client");

            edited.waterParams = unchecked((int) 0xCAFEBABE);
            Assert.True(edited.IsDirty);
            Assert.True(table.IsDirty);

            byte[] reencoded = table.Encode().ToArray();
            MaterialTable readBack = MaterialTable.Decode(new JagStream(reencoded));

            Assert.Equal(file.Length, reencoded.Length);
            Assert.Equal(unchecked((int) 0xCAFEBABE), readBack.Slots[0].waterParams);

            //The aliased byte survived, which is the whole point: it is not recoverable from the
            //bool the client decodes it into.
            Assert.Equal(AliasedBooleanByte,
                readBack.Slots[0].StoredRecord[MaterialTable.OffsetOf(MaterialColumn.Force64x64)]);

            //And the untouched record moved not at all.
            Assert.Equal(table.Slots[1].StoredRecord, readBack.Slots[1].StoredRecord);
        }

        /// <summary>
        ///     Encoding from fields loses the aliased byte, which is why it is not the write path.
        /// </summary>
        /// <remarks>
        ///     Asserted rather than merely noted, so the difference between the two encoders is
        ///     stated by a test instead of by a comment. If this ever stops differing, the stored
        ///     bytes have stopped being consulted.
        /// </remarks>
        [Fact]
        public void EncodingFromFields_CannotReproduceAnAliasedBoolean()
        {
            (_, _, byte[] file) = FileWithAnAliasedBoolean();
            MaterialTable table = MaterialTable.Decode(new JagStream(file));

            byte[] fromFields = table.EncodeFromFields().ToArray();
            MaterialTable rebuilt = MaterialTable.Decode(new JagStream(fromFields));

            int at = MaterialTable.OffsetOf(MaterialColumn.Force64x64);
            Assert.Equal(file.Length, fromFields.Length);
            Assert.Equal(AliasedBooleanByte, table.Slots[0].StoredRecord[at]);
            Assert.Equal((byte) 0, rebuilt.Slots[0].StoredRecord[at]);

            //The write path still reproduces it, which is the difference being pinned.
            Assert.Equal(file, table.Encode().ToArray());
        }

        /// <summary>
        ///     An existence byte other than 1 is an empty slot, and is written back as it was.
        /// </summary>
        /// <remarks>
        ///     <c>Class260.java:110</c> tests for exactly 1, so a 2 is a slot with no material state
        ///     and no record bytes anywhere in the nineteen columns. Recomputing the column from
        ///     "does this slot hold a definition" would turn it into a 0 and shorten nothing, which
        ///     is a silent one-byte corruption of a file that still parses.
        /// </remarks>
        [Fact]
        public void AnExistenceByteOtherThanOne_IsAbsentAndIsStoredVerbatim()
        {
            byte[][] rows = { Row(0x10), Row(0x40), Row(0x70) };
            var existence = new byte[] { 1, AliasedAbsentByte, 1 };
            byte[] file = BuildFile(existence, rows);

            MaterialTable table = MaterialTable.Decode(new JagStream(file));

            Assert.Equal(3, table.Count);
            Assert.NotNull(table.Slots[0]);
            Assert.Null(table.Slots[1]);
            Assert.NotNull(table.Slots[2]);
            Assert.Equal(file, table.Encode().ToArray());
        }

        /// <summary>Assigning a field the value it already holds is not an edit.</summary>
        /// <remarks>
        ///     A property grid writes every cell back when it commits. Counting those as edits would
        ///     rewrite the archive, and therefore its CRC and the reference-table entry of everything
        ///     packed beside it, for a dialog somebody only opened.
        /// </remarks>
        [Fact]
        public void AssigningTheValueAFieldAlreadyHolds_IsNotAnEdit()
        {
            (_, _, byte[] file) = FileWithAnAliasedBoolean();
            MaterialTable table = MaterialTable.Decode(new JagStream(file));

            foreach (TextureDefinition def in table.Slots)
            {
                int hsl = def.representativeHsl;
                bool flag = def.force64x64;
                int state = def.waterParams;
                int gain = def.colourGain;

                def.representativeHsl = hsl;
                def.force64x64 = flag;
                def.waterParams = state;
                def.colourGain = gain;
            }

            Assert.False(table.IsDirty);
            Assert.Equal(file, table.Encode().ToArray());
        }

        /// <summary>
        ///     A field set and then set back re-encodes to the bytes it was read from.
        /// </summary>
        /// <remarks>
        ///     <b>A different claim from the byte-identity sweep, and one that sweep cannot make.</b>
        ///     It proves an <i>unedited</i> record comes back as it was; this is about an edit that
        ///     nets nothing, which has to write nothing - a re-encode rewrites the archive CRC and
        ///     drags in the reference-table entry of everything packed beside it. Four defects in
        ///     this repository have lived in exactly that gap.
        /// </remarks>
        [Fact]
        public void AFieldSetAndSetBack_ReEncodesToTheStoredBytes()
        {
            (_, _, byte[] file) = FileWithAnAliasedBoolean();
            MaterialTable table = MaterialTable.Decode(new JagStream(file));

            TextureDefinition edited = table.Slots[1];
            int hsl = edited.representativeHsl;
            int gain = edited.colourGain;
            int state = edited.waterParams;

            edited.representativeHsl = hsl ^ 0x0FF0;
            edited.colourGain = ~gain & 0xFF;
            edited.waterParams = unchecked((int) 0xDEADBEEF);
            Assert.True(table.IsDirty, "the table has to notice an edit before it can notice it being undone");

            edited.representativeHsl = hsl;
            edited.colourGain = gain;
            edited.waterParams = state;

            Assert.False(table.IsDirty);
            Assert.Equal(file, table.Encode().ToArray());
        }

        /// <summary>
        ///     Unsetting a flag whose stored byte is not 0 or 1 puts that byte back, not a 0.
        /// </summary>
        /// <remarks>
        ///     The case that makes the set-and-unset check worth writing rather than assuming. A
        ///     boolean column decodes many-to-one, so a stored 2 is false and the bool cannot
        ///     reproduce it; a dirty flag that latched on the first assignment would re-encode this
        ///     column from its field and quietly normalise the byte away. Neither supported cache
        ///     carries one, so no sweep over either would ever say so.
        /// </remarks>
        [Fact]
        public void AFlagSetAndUnset_RestoresAnAliasedStoredByte()
        {
            (_, _, byte[] file) = FileWithAnAliasedBoolean();
            MaterialTable table = MaterialTable.Decode(new JagStream(file));

            TextureDefinition aliased = table.Slots[0];
            Assert.False(aliased.force64x64, "a boolean byte of 2 is false to the client");

            aliased.force64x64 = true;
            Assert.True(table.IsDirty);

            aliased.force64x64 = false;

            Assert.False(table.IsDirty);
            Assert.Equal(file, table.Encode().ToArray());
            Assert.Equal(AliasedBooleanByte,
                table.Encode().ToArray()[FileOffsetOf(MaterialColumn.Force64x64, slot: 0, slots: 2)]);
        }

        /// <summary>
        ///     Where one slot's column sits in the encoded file.
        /// </summary>
        /// <remarks>
        ///     Derived from the layout the codec states rather than counted by hand, so this stays
        ///     right if a column's width ever turns out to be something else - the widths are pinned
        ///     against the shipped file by <c>RealCacheMaterialTests</c>, not here.
        /// </remarks>
        /// <param name="column">The column.</param>
        /// <param name="slot">The slot within it.</param>
        /// <param name="slots">How many slots the file declares, all of them present.</param>
        /// <returns>The byte offset.</returns>
        private static int FileOffsetOf(MaterialColumn column, int slot, int slots)
        {
            //The count, then the existence column, then every column before this one in full.
            int at = 2 + slots;

            for (int earlier = 0; earlier < (int) column; earlier++)
                at += MaterialTable.WidthOf((MaterialColumn) earlier) * slots;

            return at + MaterialTable.WidthOf(column) * slot;
        }

        /// <summary>The decoder stops on the last column rather than on the end of the buffer.</summary>
        /// <remarks>
        ///     The synthetic half of the exact-consumption sweep. A decoder that read to the end
        ///     would accept any file length, and the padding is what makes an over-read visible
        ///     rather than merely undetected.
        /// </remarks>
        [Fact]
        public void DecodeStopsOnTheLastColumn_AndConsumesNothingPastIt()
        {
            (_, _, byte[] file) = FileWithAnAliasedBoolean();
            var padded = new byte[file.Length + 16];
            Array.Copy(file, padded, file.Length);
            for (int i = file.Length; i < padded.Length; i++)
                padded[i] = 0xAA;

            var stream = new JagStream(padded);
            MaterialTable.Decode(stream);

            Assert.Equal(file.Length, stream.Position);
            Assert.Equal(2 + 2 + 2 * MaterialTable.BytesPerRecord, file.Length);
        }

        /// <summary>
        ///     The manager's encoder reflects an edit rather than replaying the captured blob.
        /// </summary>
        /// <remarks>
        ///     This is the defect that made the whole index read-only in practice:
        ///     <c>EncodeColumnar</c> returned <c>RawIndexData</c> whenever it was non-null, and the
        ///     load path always sets it, so every field edit was discarded in silence. A test that
        ///     only round-tripped an unedited table could never have said so - it passed throughout.
        /// </remarks>
        [Fact]
        public void EncodeColumnar_ReflectsAnEdit_RatherThanReplayingTheCapturedBlob()
        {
            (_, _, byte[] file) = FileWithAnAliasedBoolean();

            TextureManager.Clear();
            TextureManager.RawIndexData = file;
            TextureManager.DecodeColumnar(new JagStream(file));

            Assert.Equal(file, TextureManager.EncodeColumnar().ToArray());

            //The dictionary the editor edits through and the table the write path encodes are the
            //same objects. If they ever stop being, an edit reaches one and the save reads the other.
            Assert.Same(TextureManager.Materials.Slots[1], TextureManager.Textures[1]);

            TextureManager.Textures[1].representativeHsl = 0x1234;

            byte[] reencoded = TextureManager.EncodeColumnar().ToArray();
            Assert.NotEqual(file, reencoded);

            MaterialTable stored = MaterialTable.Decode(new JagStream(file));
            MaterialTable readBack = MaterialTable.Decode(new JagStream(reencoded));

            Assert.Equal(0x1234, readBack.Slots[1].representativeHsl);
            Assert.Equal(stored.Slots[0].StoredRecord, readBack.Slots[0].StoredRecord);

            TextureManager.Clear();
        }
    }
}
