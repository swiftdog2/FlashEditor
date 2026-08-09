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
        private const byte AliasedBooleanByte = 2;

        /// <summary>An existence byte the client reads as an empty slot, being anything but 1.</summary>
        private const byte AliasedAbsentByte = 2;

        /// <summary>
        ///     Interleaves whole records into the column-major file the client reads.
        /// </summary>
        /// <remarks>
        ///     Takes each record as its 23 stored bytes, so the only thing it borrows from the codec
        ///     is where one column sits inside a record. What is being asserted below is which of
        ///     those bytes survive an edit, and that is independent of where they sit.
        /// </remarks>
        /// <param name="existence">One existence byte per slot.</param>
        /// <param name="rows">One 23-byte record per slot, ignored where the slot is absent.</param>
        /// <returns>The encoded file.</returns>
        private static byte[] BuildFile(byte[] existence, byte[][] rows)
        {
            var stream = new JagStream();
            stream.WriteShort(existence.Length);

            foreach (byte flag in existence)
                stream.WriteByte(flag);

            for (int column = 0; column < MaterialTable.ColumnCount; column++)
            {
                int offset = MaterialTable.OffsetOf((MaterialColumn) column);
                int width = MaterialTable.WidthOf((MaterialColumn) column);

                for (int slot = 0; slot < existence.Length; slot++)
                    if (existence[slot] == 1)
                        stream.Write(rows[slot], offset, width);
            }

            stream.Flip();
            return stream.ToArray();
        }

        /// <summary>A record whose every byte is distinct, so a misplaced column is visible.</summary>
        /// <param name="seed">Value of the record's first byte.</param>
        /// <returns>The 23 stored bytes.</returns>
        private static byte[] Row(int seed)
        {
            var row = new byte[MaterialTable.BytesPerRecord];
            for (int i = 0; i < row.Length; i++)
                row[i] = (byte) (seed + i);
            return row;
        }

        /// <summary>A two-slot file whose first record carries a boolean byte no cache holds.</summary>
        /// <returns>The existence column, the rows and the encoded file.</returns>
        private static (byte[] Existence, byte[][] Rows, byte[] File) FileWithAnAliasedBoolean()
        {
            byte[][] rows = { Row(0x10), Row(0x40) };
            rows[0][MaterialTable.OffsetOf(MaterialColumn.Field1822)] = AliasedBooleanByte;

            var existence = new byte[] { 1, 1 };
            return (existence, rows, BuildFile(existence, rows));
        }

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
            Assert.False(edited.field1822, "a boolean byte of 2 is false to the client");

            edited.field1835 = unchecked((int) 0xCAFEBABE);
            Assert.True(edited.IsDirty);
            Assert.True(table.IsDirty);

            byte[] reencoded = table.Encode().ToArray();
            MaterialTable readBack = MaterialTable.Decode(new JagStream(reencoded));

            Assert.Equal(file.Length, reencoded.Length);
            Assert.Equal(unchecked((int) 0xCAFEBABE), readBack.Slots[0].field1835);

            //The aliased byte survived, which is the whole point: it is not recoverable from the
            //bool the client decodes it into.
            Assert.Equal(AliasedBooleanByte,
                readBack.Slots[0].StoredRecord[MaterialTable.OffsetOf(MaterialColumn.Field1822)]);

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

            int at = MaterialTable.OffsetOf(MaterialColumn.Field1822);
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
                int hsl = def.field1831;
                bool flag = def.field1822;
                int state = def.field1835;
                sbyte signed = def.field1829;

                def.field1831 = hsl;
                def.field1822 = flag;
                def.field1835 = state;
                def.field1829 = signed;
            }

            Assert.False(table.IsDirty);
            Assert.Equal(file, table.Encode().ToArray());
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

            TextureManager.Textures[1].field1831 = 0x1234;

            byte[] reencoded = TextureManager.EncodeColumnar().ToArray();
            Assert.NotEqual(file, reencoded);

            MaterialTable stored = MaterialTable.Decode(new JagStream(file));
            MaterialTable readBack = MaterialTable.Decode(new JagStream(reencoded));

            Assert.Equal(0x1234, readBack.Slots[1].field1831);
            Assert.Equal(stored.Slots[0].StoredRecord, readBack.Slots[0].StoredRecord);

            TextureManager.Clear();
        }
    }
}
