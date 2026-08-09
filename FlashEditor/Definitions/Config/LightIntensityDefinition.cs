using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A light intensity curve: the waveform, rate, amplitude and offset that make a light pulse.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.LightIntensity"/>. Decoded by
    ///     <c>Class379.method4008</c> (:39-56) dispatching to <c>method4009</c> (:58-81); the provider
    ///     is <c>Class269</c>, which names the group at Class269.java:161.
    ///     <para>
    ///     <b>Settled by usage.</b> An effect stream that meets a marker of 31 reads a record id and
    ///     hands all four fields to <c>Class1.method166</c> (Class305_Sub1.java:296-299 and 510-513).
    ///     <c>Class1.method163</c> (:185-253) then evaluates, once per tick,
    ///     <c>phase = 0x7ff &amp; (base + tick * rate / 50)</c>, selects a waveform on
    ///     <see cref="Waveform"/>, and writes <c>(offset + (wave * amplitude &gt;&gt; 11)) / 2048f</c>
    ///     into <c>Node_Sub5.method959</c>. Every <c>Node_Sub5</c> subclass stores that float in
    ///     <c>aFloat3832</c>, the light's intensity, which is what makes this a light curve rather
    ///     than a generic value curve.
    ///     </para>
    ///     <para>
    ///     Four files, and <b>none of the four is in ascending opcode order</b> - every one stores
    ///     3, 2, 4, 1. It is the cheapest case in the cache that an ascending encoder would fail on.
    ///     </para>
    /// </remarks>
    public sealed class LightIntensityDefinition : ConfigDefinition {
        /// <summary>Opcode 1. Which waveform the phase is shaped by.</summary>
        /// <remarks>
        ///     <c>anInt3195</c> to <c>Class1.anInt53</c>. The arms at Class1.java:206-245 are:
        ///     1 a sine table lookup biased by 1024, 2 the raw phase, 3 a second table halved,
        ///     4 the phase squared off to <c>phase &gt;&gt; 10 &lt;&lt; 11</c>, 5 a triangle, and
        ///     anything else a constant 2048. Measured over both caches: value 3 on one record and 1
        ///     on the other three.
        /// </remarks>
        public int Waveform { get; set; }

        /// <summary>Opcode 2. How fast the phase advances, in phase units per 50 ticks.</summary>
        /// <remarks>
        ///     <c>anInt3197</c> to <c>Class1.anInt60</c>. Defaults to 2048, which is the value the
        ///     client's constructor leaves it at, so absent and a stored 2048 are indistinguishable
        ///     to it and presence is read off the opcode list.
        /// </remarks>
        public int Rate { get; set; } = 2048;

        /// <summary>Opcode 3. What the waveform's output is scaled by before the offset is added.</summary>
        /// <remarks><c>anInt3194</c> to <c>Class1.anInt52</c>. Also defaults to 2048.</remarks>
        public int Amplitude { get; set; } = 2048;

        /// <summary>Opcode 4. The intensity the waveform swings about, signed.</summary>
        /// <remarks>
        ///     <c>anInt3193</c> to <c>Class1.anInt56</c>, read with <c>readShort</c> so it is signed.
        ///     Measured 0, 410, 819 and 1229 in this cache, all positive, so the sign is settled by
        ///     the client's reader alone.
        /// </remarks>
        public int Offset { get; set; }

        /// <summary>Decodes one light intensity definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public LightIntensityDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: Waveform = stream.ReadUnsignedByte(); break;
                case 2: Rate = stream.ReadUnsignedShort(); break;
                case 3: Amplitude = stream.ReadUnsignedShort(); break;
                case 4: Offset = stream.ReadShort(); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: stream.WriteByte(Waveform); break;
                case 2: stream.WriteShort(Rate); break;
                case 3: stream.WriteShort(Amplitude); break;
                case 4: stream.WriteShort(Offset); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && Waveform != 0) yield return 1;
            if (!Has(2) && Rate != 2048) yield return 2;
            if (!Has(3) && Amplitude != 2048) yield return 3;
            if (!Has(4) && Offset != 0) yield return 4;
        }
    }
}
