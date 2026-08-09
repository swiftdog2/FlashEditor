using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.Definitions.Particles;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Particles
{
    /// <summary>
    ///     Index-27 codec tests over hand-laid bytes, for the branches the cache cannot speak for.
    /// </summary>
    /// <remarks>
    ///     Every record here is written out field by field in the order the 637 client reads it,
    ///     rather than produced by this project's encoder, so the assertions are against the format
    ///     and not against the codec agreeing with itself. That matters most for the opcodes neither
    ///     supported cache stores: emitter 2, 11, 17, 25 and 29 and effector 2, 5 and 7 occur nowhere,
    ///     so the real-cache sweeps pass whatever this codec does with them.
    /// </remarks>
    public sealed class ParticleDefinitionCodecTests
    {
        // ===================================================================
        //  Emitters
        // ===================================================================

        /// <summary>
        ///     Opcodes 5 and 31 are two encodings of the same pair, and which one was stored is kept.
        /// </summary>
        /// <remarks>
        ///     Opcode 5 sets both bounds from one value (ParticleType.java:541) and opcode 31 gives
        ///     each its own (:593-596). Decoding to the values alone throws away which was on disk,
        ///     and a record that stored opcode 5 then re-encodes two bytes longer.
        /// </remarks>
        [Fact]
        public void EmitterSizeAlias_KeepsWhicheverEncodingWasStored()
        {
            byte[] single = Bytes(writer =>
            {
                writer.WriteByte(5);
                writer.WriteShort(100);
            });

            var fromSingle = new ParticleEmitterDefinition().Decode(new JagStream(single));
            Assert.Equal(100, fromSingle.SizeMinStored);
            Assert.Equal(100, fromSingle.SizeMaxStored);
            Assert.True(fromSingle.StoresSizeAsASingleValue);
            Assert.Equal(single, fromSingle.Encode().ToArray());

            byte[] pair = Bytes(writer =>
            {
                writer.WriteByte(31);
                writer.WriteShort(100);
                writer.WriteShort(200);
            });

            var fromPair = new ParticleEmitterDefinition().Decode(new JagStream(pair));
            Assert.Equal(100, fromPair.SizeMinStored);
            Assert.Equal(200, fromPair.SizeMaxStored);
            Assert.False(fromPair.StoresSizeAsASingleValue);
            Assert.Equal(pair, fromPair.Encode().ToArray());
        }

        /// <summary>
        ///     Pulling the two size bounds apart moves a record off opcode 5, which cannot hold them.
        /// </summary>
        /// <remarks>
        ///     The alternative is the silent failure: opcode 5 replayed as it was read, the editor
        ///     showing two different bounds, the save reporting success and the client loading one
        ///     value for both.
        /// </remarks>
        [Fact]
        public void EditingOneSizeBoundApartFromTheOtherSwitchesToTheTwoValueOpcode()
        {
            byte[] single = Bytes(writer =>
            {
                writer.WriteByte(5);
                writer.WriteShort(100);
            });

            var definition = new ParticleEmitterDefinition().Decode(new JagStream(single));
            definition.SizeMaxStored = 200;

            byte[] encoded = definition.Encode().ToArray();
            var reread = new ParticleEmitterDefinition().Decode(new JagStream(encoded));

            Assert.Equal(100, reread.SizeMinStored);
            Assert.Equal(200, reread.SizeMaxStored);
            Assert.False(reread.StoresSizeAsASingleValue);
            Assert.DoesNotContain(5, reread.Opcodes.Select(record => record.Opcode));
        }

        /// <summary>A payload-free opcode stored twice survives, because neither occurrence is a value.</summary>
        /// <remarks>
        ///     Opcode 24 carries nothing at all (ParticleType.java:583-584), so a record storing it
        ///     twice is literally the same byte twice. A codec holding only a bool re-encodes one.
        /// </remarks>
        [Fact]
        public void ARepeatedBareFlagIsWrittenBackAsManyTimesAsItWasStored()
        {
            byte[] twice = Bytes(writer =>
            {
                writer.WriteByte(24);
                writer.WriteByte(24);
            });

            var definition = new ParticleEmitterDefinition().Decode(new JagStream(twice));

            Assert.True(definition.RandomisesColourChannelsIndependently);
            Assert.Equal(2, definition.Opcodes.Select(entry => entry.Opcode).Count(opcode => opcode == 24));
            Assert.Equal(twice, definition.Encode().ToArray());
        }

        /// <summary>
        ///     Emitter opcode 11 consumes nothing, so the byte after it is the next opcode.
        /// </summary>
        /// <remarks>
        ///     It falls through every branch of <c>ParticleType.method895</c> with no handler. Giving
        ///     it a width would read opcode 15's first payload byte as padding and every field after
        ///     it would be garbage - and neither supported cache stores opcode 11, so no sweep here
        ///     would notice.
        /// </remarks>
        [Fact]
        public void EmitterOpcode11ReadsNoPayloadAtAll()
        {
            byte[] record = Bytes(writer =>
            {
                writer.WriteByte(11);
                writer.WriteByte(15);
                writer.WriteShort(4242);
            });

            var definition = new ParticleEmitterDefinition().Decode(new JagStream(record));

            Assert.True(definition.HasUnhandledFlag11);
            Assert.Equal(4242, definition.MaterialId);
            Assert.Equal(record, definition.Encode().ToArray());
        }

        /// <summary>
        ///     The two opcodes whose value the client discards are still stored and written back.
        /// </summary>
        /// <remarks>
        ///     Opcode 2 is a bare <c>readUnsignedByte</c> (ParticleType.java:534) and opcode 29 a bare
        ///     <c>readShort</c> (:610). Neither reaches a field in the client and neither occurs in
        ///     either supported cache, so only this test says the bytes survive a save.
        /// </remarks>
        [Fact]
        public void EmitterOpcodesWhoseValueTheClientDiscardsStillRoundTrip()
        {
            byte[] record = Bytes(writer =>
            {
                writer.WriteByte(2);
                writer.WriteByte(0x5A);
                writer.WriteByte(29);
                writer.WriteShort(-4242);
            });

            var definition = new ParticleEmitterDefinition().Decode(new JagStream(record));

            Assert.Equal(0x5A, definition.UnusedByte2);
            Assert.Equal(-4242, definition.UnusedShort29);
            Assert.Equal(record, definition.Encode().ToArray());
        }

        /// <summary>Emitter opcode 17 and 25, which no cache stores, read the widths the client reads.</summary>
        [Fact]
        public void EmitterOpcodesAbsentFromTheCacheReadTheWidthsTheClientReads()
        {
            byte[] record = Bytes(writer =>
            {
                writer.WriteByte(17);
                writer.WriteShort(321);
                writer.WriteByte(25);
                writer.WriteByte(3);
                writer.WriteShort(11);
                writer.WriteShort(22);
                writer.WriteShort(33);
            });

            var definition = new ParticleEmitterDefinition().Decode(new JagStream(record));

            Assert.Equal(321, definition.LowDetailEmitterId);
            Assert.Equal(new[] { 11, 22, 33 }, definition.AttachedEffectorKeys);
            Assert.Equal(record, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A record's stored opcode order is replayed rather than sorted.
        /// </summary>
        /// <remarks>
        ///     Every record in this index stores its opcodes out of ascending order, so an encoder
        ///     with an order of its own reproduces none of them - and every such rewrite changes the
        ///     archive CRC and the reference-table entry of everything packed beside it.
        /// </remarks>
        [Fact]
        public void EmitterOpcodeOrderIsReplayedRatherThanSorted()
        {
            byte[] record = Bytes(writer =>
            {
                writer.WriteByte(30);
                writer.WriteByte(15);
                writer.WriteShort(7);
                writer.WriteByte(3);
                writer.WriteInteger(64);
                writer.WriteInteger(128);
            });

            var definition = new ParticleEmitterDefinition().Decode(new JagStream(record));

            Assert.Equal(new[] { 30, 15, 3 }, definition.Opcodes.Select(entry => entry.Opcode).ToArray());
            Assert.Equal(record, definition.Encode().ToArray());
        }

        /// <summary>Every emitter field the client reads comes back in the order and width it was written.</summary>
        /// <remarks>
        ///     Distinct values throughout, so a pair read the wrong way round fails rather than
        ///     passing on two equal numbers.
        /// </remarks>
        [Fact]
        public void EmitterMultiFieldOpcodesReadTheirFieldsInTheClientsOrder()
        {
            byte[] record = Bytes(writer =>
            {
                writer.WriteByte(1);
                writer.WriteShort(11);
                writer.WriteShort(22);
                writer.WriteShort(33);
                writer.WriteShort(44);

                writer.WriteByte(4);
                writer.WriteByte(2);
                writer.WriteSignedByte(-5);

                writer.WriteByte(16);
                writer.WriteByte(1);
                writer.WriteShort(300);
                writer.WriteShort(900);
                writer.WriteByte(0);

                writer.WriteByte(7);
                writer.WriteShort(60);
                writer.WriteShort(120);
            });

            var definition = new ParticleEmitterDefinition().Decode(new JagStream(record));

            Assert.Equal(11, definition.YawStartStored);
            Assert.Equal(22, definition.YawEndStored);
            Assert.Equal(33, definition.PitchStartStored);
            Assert.Equal(44, definition.PitchEndStored);

            Assert.Equal(2, definition.DragMode);
            Assert.Equal(-5, definition.DragStrength);

            Assert.Equal(1, definition.CycleFlagStored);
            Assert.True(definition.EmitsBeforeThreshold);
            Assert.Equal(300, definition.CycleThreshold);
            Assert.Equal(900, definition.CyclePeriod);
            Assert.Equal(0, definition.CycleRepeatsStored);
            Assert.False(definition.CycleRepeats);

            Assert.Equal(60, definition.LifetimeMin);
            Assert.Equal(120, definition.LifetimeMax);

            Assert.Equal(record, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A stored flag byte the client compares against 1 keeps its value rather than its meaning.
        /// </summary>
        /// <remarks>
        ///     ParticleType.java:648-651 tests both of opcode 16's bytes with <c>== 1</c>, so a stored
        ///     2 means false to the client and re-encodes as 2 here. Recomputing it from the bool
        ///     would write a 0 and change a file nobody edited. Neither cache stores a value outside
        ///     0 and 1, so this branch is defended by nothing else.
        /// </remarks>
        [Fact]
        public void EmitterCycleFlagBytesKeepTheirStoredValue()
        {
            byte[] record = Bytes(writer =>
            {
                writer.WriteByte(16);
                writer.WriteByte(2);
                writer.WriteShort(1);
                writer.WriteShort(2);
                writer.WriteByte(7);
            });

            var definition = new ParticleEmitterDefinition().Decode(new JagStream(record));

            Assert.Equal(2, definition.CycleFlagStored);
            Assert.False(definition.EmitsBeforeThreshold);
            Assert.Equal(7, definition.CycleRepeatsStored);
            Assert.False(definition.CycleRepeats);
            Assert.Equal(record, definition.Encode().ToArray());
        }

        /// <summary>An opcode with no known width is reported rather than skipped.</summary>
        [Fact]
        public void AnUnknownEmitterOpcodeThrowsRatherThanDesynchronising()
        {
            byte[] record = { 35, 0, 0, 0 };

            Assert.Throws<InvalidOperationException>(
                () => new ParticleEmitterDefinition().Decode(new JagStream(record)));
        }

        /// <summary>A clone does not share the recorded opcode stream with the definition it came from.</summary>
        [Fact]
        public void CloningAnEmitterDetachesItsOpcodeStream()
        {
            byte[] record = Bytes(writer => writer.WriteByte(30));

            var original = new ParticleEmitterDefinition().Decode(new JagStream(record));
            ParticleEmitterDefinition copy = original.Clone();
            copy.KeepsMaterialOnSoftwareRenderer = false;

            Assert.True(original.KeepsMaterialOnSoftwareRenderer);
            Assert.False(copy.KeepsMaterialOnSoftwareRenderer);
        }

        // ===================================================================
        //  Effectors
        // ===================================================================

        /// <summary>
        ///     The falloff mode and strength sit on opcode 4, not opcode 5.
        /// </summary>
        /// <remarks>
        ///     The client spells the test <c>(i ^ 0xffffffff) == -5</c>, which is <c>i == 4</c>
        ///     (Class66.java:293). Reading it as opcode 5 mis-sizes every record in this cache that
        ///     carries either, and opcode 5 has no handler at all.
        /// </remarks>
        [Fact]
        public void EffectorFalloffIsOpcodeFourAndOpcodeFiveHasNoPayload()
        {
            byte[] withFalloff = Bytes(writer =>
            {
                writer.WriteByte(4);
                writer.WriteByte(2);
                writer.WriteInteger(4096);
            });

            var falloff = new ParticleEffectorDefinition().Decode(new JagStream(withFalloff));
            Assert.Equal(2, falloff.FalloffMode);
            Assert.Equal(4096, falloff.Strength);
            Assert.Equal(withFalloff, falloff.Encode().ToArray());

            byte[] withUnhandledFive = Bytes(writer =>
            {
                writer.WriteByte(5);
                writer.WriteByte(6);
                writer.WriteByte(2);
            });

            var unhandled = new ParticleEffectorDefinition().Decode(new JagStream(withUnhandledFive));
            Assert.True(unhandled.HasUnhandledFlag5);
            Assert.Equal(ParticleEffectorDefinition.GlobalMode, unhandled.Mode);
            Assert.Equal(withUnhandledFive, unhandled.Encode().ToArray());
        }

        /// <summary>Opcode 7 is the other unhandled, payload-free effector opcode.</summary>
        [Fact]
        public void EffectorOpcode7ReadsNoPayloadAtAll()
        {
            byte[] record = Bytes(writer =>
            {
                writer.WriteByte(7);
                writer.WriteByte(1);
                writer.WriteShort(512);
            });

            var definition = new ParticleEffectorDefinition().Decode(new JagStream(record));

            Assert.True(definition.HasUnhandledFlag7);
            Assert.Equal(512, definition.ConeAngleStored);
            Assert.Equal(record, definition.Encode().ToArray());
        }

        /// <summary>The direction vector's three components come back in the order they were written.</summary>
        [Fact]
        public void EffectorDirectionIsReadAsThreeSignedIntegersInOrder()
        {
            byte[] record = Bytes(writer =>
            {
                writer.WriteByte(3);
                writer.WriteInteger(-1);
                writer.WriteInteger(2);
                writer.WriteInteger(-3);
            });

            var definition = new ParticleEffectorDefinition().Decode(new JagStream(record));

            Assert.Equal(-1, definition.DirectionX);
            Assert.Equal(2, definition.DirectionY);
            Assert.Equal(-3, definition.DirectionZ);
            Assert.Equal(record, definition.Encode().ToArray());
        }

        /// <summary>An effector opcode with no known width is reported rather than skipped.</summary>
        [Fact]
        public void AnUnknownEffectorOpcodeThrowsRatherThanDesynchronising()
        {
            byte[] record = { 11, 0, 0 };

            Assert.Throws<InvalidOperationException>(
                () => new ParticleEffectorDefinition().Decode(new JagStream(record)));
        }

        /// <summary>Lays out a record and appends the opcode 0 terminator.</summary>
        /// <param name="write">Writes the opcodes and their payloads.</param>
        /// <returns>The record bytes.</returns>
        private static byte[] Bytes(Action<JagStream> write)
        {
            var stream = new JagStream();
            write(stream);
            stream.WriteByte(0);
            return stream.Flip().ToArray();
        }
    }
}
