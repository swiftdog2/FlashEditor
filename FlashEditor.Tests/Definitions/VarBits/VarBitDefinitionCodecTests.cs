using System;
using FlashEditor.Definitions.VarBits;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.VarBits
{
    /// <summary>
    ///     The varbit codec against bytes lifted from a real revision-639 cache.
    /// </summary>
    /// <remarks>
    ///     The format is one opcode with three fields, so what is worth pinning offline is not the
    ///     parse but the two states that decode alike: a stored record whose fields are all zero,
    ///     and a file holding only the terminator. They are different bytes and must stay so.
    /// </remarks>
    public sealed class VarBitDefinitionCodecTests
    {
        /// <summary>Varbit 0: varp 318, a single bit at position 0.</summary>
        /// <remarks>
        ///     Both bit positions are 0, so the record's payload is indistinguishable by value from
        ///     a default-valued varbit - and it is six bytes rather than one.
        /// </remarks>
        public static readonly byte[] SingleBitRecord = { 0x01, 0x01, 0x3E, 0x00, 0x00, 0x00 };

        /// <summary>Varbit 6: a file holding nothing but the terminator.</summary>
        public static readonly byte[] BareTerminator = { 0x00 };

        /// <summary>Varbit 11: varp 340, bits 14 to 27 - a fourteen-bit range.</summary>
        public static readonly byte[] WideRangeRecord = { 0x01, 0x01, 0x54, 0x0E, 0x1B, 0x00 };

        /// <summary>A stored record decodes to its three fields and consumes its buffer exactly.</summary>
        [Fact]
        public void AStoredRecordDecodesToItsFields()
        {
            var stream = new JagStream(WideRangeRecord);
            var definition = new VarBitDefinition { Id = 11 }.Decode(stream);

            Assert.Equal(WideRangeRecord.Length, stream.Position);
            Assert.True(definition.IsStored);
            Assert.Equal(340, definition.VarpId);
            Assert.Equal(14, definition.FromBit);
            Assert.Equal(27, definition.ToBit);
            Assert.Equal(WideRangeRecord, definition.Encode().ToArray());
        }

        /// <summary>
        ///     The mask and the extraction match what the client computes for the same record.
        /// </summary>
        /// <remarks>
        ///     <c>Class140.method2289</c> masks with <c>anIntArray6070[toBit - fromBit]</c> and
        ///     shifts left by <c>fromBit</c>, and that table holds <c>2^(n+1) - 1</c>. Bits 14..27
        ///     are therefore fourteen bits wide with a mask of 0x3FFF, which is the arithmetic an
        ///     editor showing a derived width has to get right.
        /// </remarks>
        [Fact]
        public void TheMaskAndExtractionFollowTheClientsArithmetic()
        {
            var definition = new VarBitDefinition { Id = 11 }.Decode(new JagStream(WideRangeRecord));

            Assert.Equal(14, definition.BitWidth);
            Assert.Equal(0x3FFF, definition.Mask);
            Assert.True(definition.FitsTheClientMaskTable);

            //Bits 14..27 carry 0x2A5B; everything below bit 14 and above bit 27 is noise the
            //extraction has to drop, which is what the shift and the mask are each responsible for.
            int varp = (1 << 28) | (0x2A5B << 14) | 0x3FFF;
            Assert.Equal(0x2A5B, definition.Extract(varp));
            Assert.Equal(0, definition.Extract(0x3FFF));
        }

        /// <summary>
        ///     A single-bit record at position 0 still writes six bytes, not one.
        /// </summary>
        /// <remarks>
        ///     Its fields are all zero, so an encoder deciding what to write from the values alone
        ///     would collapse it to a bare terminator and shorten the file.
        /// </remarks>
        [Fact]
        public void AnAllZeroStoredRecordKeepsItsOpcode()
        {
            var definition = new VarBitDefinition { Id = 0 }.Decode(new JagStream(SingleBitRecord));

            Assert.True(definition.IsStored);
            Assert.Equal(318, definition.VarpId);
            Assert.Equal(0, definition.FromBit);
            Assert.Equal(0, definition.ToBit);
            Assert.Equal(1, definition.BitWidth);
            Assert.Equal(SingleBitRecord, definition.Encode().ToArray());
        }

        /// <summary>A bare terminator stays one byte.</summary>
        [Fact]
        public void ABareTerminatorStaysOneByte()
        {
            var stream = new JagStream(BareTerminator);
            var definition = new VarBitDefinition { Id = 6 }.Decode(stream);

            Assert.Equal(BareTerminator.Length, stream.Position);
            Assert.False(definition.IsStored);
            Assert.Equal(0, definition.VarpId);
            Assert.Equal(BareTerminator, definition.Encode().ToArray());
        }

        /// <summary>Editing a bare-terminator varbit materialises the opcode.</summary>
        /// <remarks>
        ///     The other half of absent-versus-default: keeping the file at one byte is correct only
        ///     until someone actually sets a field, and an encoder driven purely by the recorded
        ///     stream would drop the edit silently.
        /// </remarks>
        [Fact]
        public void EditingABareTerminatorMaterialisesTheRecord()
        {
            var definition = new VarBitDefinition { Id = 6 }.Decode(new JagStream(BareTerminator));
            definition.VarpId = 1234;
            definition.FromBit = 3;
            definition.ToBit = 9;

            byte[] encoded = definition.Encode().ToArray();
            var reread = new VarBitDefinition { Id = 6 }.Decode(new JagStream(encoded));

            Assert.Equal(new byte[] { 0x01, 0x04, 0xD2, 0x03, 0x09, 0x00 }, encoded);
            Assert.True(reread.IsStored);
            Assert.Equal(1234, reread.VarpId);
            Assert.Equal(3, reread.FromBit);
            Assert.Equal(9, reread.ToBit);
        }

        /// <summary>
        ///     A bit range wider than the client's mask table is reported rather than silently
        ///     accepted.
        /// </summary>
        /// <remarks>
        ///     <c>anIntArray6070</c> has 32 entries and the client does not bounds-check the index,
        ///     so a 33-bit range crashes it at load. Nothing in either supported cache is that wide,
        ///     which is exactly why the check has to be stated here rather than left to a sweep.
        /// </remarks>
        [Fact]
        public void AnOverWideRangeDoesNotFitTheClientMaskTable()
        {
            var definition = new VarBitDefinition { Id = 0 };
            definition.Decode(new JagStream(new byte[] { 0x01, 0x00, 0x01, 0x00, 0x20, 0x00 }));

            Assert.Equal(32, definition.ToBit);
            Assert.Equal(33, definition.BitWidth);
            Assert.False(definition.FitsTheClientMaskTable);
        }

        /// <summary>An opcode the client does not handle is refused rather than desynchronising.</summary>
        /// <remarks>
        ///     <c>VarBit.method3946</c> handles opcode 1 and nothing else, consuming nothing for any
        ///     other value, so its loop reads the next payload byte as an opcode and cannot notice.
        /// </remarks>
        [Fact]
        public void UnknownOpcodesAreRefused()
        {
            foreach (byte opcode in new byte[] { 2, 3, 255 })
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new VarBitDefinition { Id = 0 }.Decode(new JagStream(new byte[] { opcode, 0, 0 })));
            }
        }
    }
}
