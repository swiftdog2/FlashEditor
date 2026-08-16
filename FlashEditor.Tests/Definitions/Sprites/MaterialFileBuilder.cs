using FlashEditor.Definitions.Sprites;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     Builds index-26 files by hand, for the claims no sweep over either shipped cache can make.
    /// </summary>
    /// <remarks>
    ///     One copy rather than one per test class. It interleaves whole records into the
    ///     column-major layout the client reads, taking each record as its 23 stored bytes, so the
    ///     only thing it borrows from the codec is where a column sits inside a record - which is
    ///     what the byte-identity sweep over the shipped file already settles.
    /// </remarks>
    internal static class MaterialFileBuilder
    {
        /// <summary>A boolean byte no supported cache holds, which decodes to false and cannot be rebuilt.</summary>
        internal const byte AliasedBooleanByte = 2;

        /// <summary>An existence byte the client reads as an empty slot, being anything but 1.</summary>
        internal const byte AliasedAbsentByte = 2;

        /// <summary>Interleaves whole records into the column-major file the client reads.</summary>
        /// <param name="existence">One existence byte per slot.</param>
        /// <param name="rows">One 23-byte record per slot, ignored where the slot is absent.</param>
        /// <returns>The encoded file.</returns>
        internal static byte[] BuildFile(byte[] existence, byte[][] rows)
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
        internal static byte[] Row(int seed)
        {
            var row = new byte[MaterialTable.BytesPerRecord];
            for (int i = 0; i < row.Length; i++)
                row[i] = (byte) (seed + i);
            return row;
        }

        /// <summary>A two-slot file whose first record carries a boolean byte no cache holds.</summary>
        /// <returns>The existence column, the rows and the encoded file.</returns>
        internal static (byte[] Existence, byte[][] Rows, byte[] File) FileWithAnAliasedBoolean()
        {
            byte[][] rows = { Row(0x10), Row(0x40) };
            rows[0][MaterialTable.OffsetOf(MaterialColumn.Field1822)] = AliasedBooleanByte;

            var existence = new byte[] { 1, 1 };
            return (existence, rows, BuildFile(existence, rows));
        }
    }
}
