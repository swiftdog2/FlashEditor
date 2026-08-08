using System;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     One shared amplitude/vibrato shape that a patch's keys point at, the client's
    ///     <c>Class89</c>.
    /// </summary>
    /// <remarks>
    ///     A patch declares a handful of these and every key names one, so an instrument whose
    ///     128 keys all fade the same way stores that fade once. The client keeps the whole thing
    ///     as five loose fields and two interleaved <c>byte[]</c>s; the split into named members
    ///     here is only a presentation of the same bytes.
    ///     <para>
    ///     Both envelopes are breakpoint lists, evaluated at <c>Node_Sub31_Sub2.java:421-434</c>
    ///     (attack) and <c>:436-448</c> (release) by linear interpolation between neighbouring
    ///     points, the result scaling the voice's gain by <c>level / 64</c>. The attack list runs
    ///     from note-on; the release list runs only once the voice is released
    ///     (<c>:436</c> gates it on the release counter being positive).
    ///     </para>
    ///     <para>
    ///     <b>Times are held as the deltas the file stores, not as absolute positions.</b> The
    ///     client accumulates each chain in an <c>int</c> and then stores every step through a
    ///     <c>byte</c> cast (<c>Node_Sub44.java:318-321</c>, <c>:328-332</c>), so a chain that
    ///     passes 255 keeps counting in the accumulator while the stored bytes wrap. Absolute
    ///     positions recovered from those bytes therefore cannot be turned back into the deltas
    ///     that produced them, and an encoder built on them would rewrite files nobody edited.
    ///     </para>
    /// </remarks>
    public sealed class MidiPatchEnvelope {
        /// <summary>
        ///     Levels of the attack envelope's breakpoints, one per point.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:293-295</c> fills the odd slots of <c>aByteArray714</c>. Signed,
        ///     because the client reads them with <c>readSignedByte</c> and interpolates in
        ///     <c>int</c> arithmetic at <c>Node_Sub31_Sub2.java:423-430</c>.
        /// </remarks>
        public sbyte[] AttackLevels { get; set; } = Array.Empty<sbyte>();

        /// <summary>
        ///     Gaps between the attack breakpoints, as stored: one fewer than there are points.
        /// </summary>
        /// <remarks>
        ///     The first point sits at time 0 and is not stored. Each further point advances by
        ///     <c>1 + delta</c> (<c>Node_Sub44.java:330</c>), so a delta of 0 is a step of one and
        ///     the chain is strictly increasing.
        /// </remarks>
        public byte[] AttackTimeDeltas { get; set; } = Array.Empty<byte>();

        /// <summary>
        ///     Levels of the release envelope's breakpoints, excluding its first and last.
        /// </summary>
        /// <remarks>
        ///     The list is deliberately short by two. The client presets the first level to 64
        ///     (<c>Node_Sub44.java:178</c>) and never writes the last slot at all, so the release
        ///     always starts at unity gain and ends at silence and neither end is stored.
        /// </remarks>
        public sbyte[] ReleaseLevels { get; set; } = Array.Empty<sbyte>();

        /// <summary>Gaps between the release breakpoints, as stored, one per declared point.</summary>
        /// <remarks><c>Node_Sub44.java:319</c>, same <c>1 + delta</c> chain as the attack list.</remarks>
        public byte[] ReleaseTimeDeltas { get; set; } = Array.Empty<byte>();

        /// <summary>
        ///     How steeply the voice decays while held, 0 for not at all.
        /// </summary>
        /// <remarks>
        ///     <c>Class89.anInt707</c>, applied as <c>pow(0.5, elapsed * decay * 1.953125e-5)</c>
        ///     at <c>Node_Sub31_Sub2.java:416-419</c>. Its being zero also removes
        ///     <see cref="DecayRate"/> from the file, which is why it has to be decoded before the
        ///     later planes can be read at all.
        /// </remarks>
        public int Decay { get; set; }

        /// <summary>
        ///     How fast the attack envelope is walked, or -1 when the patch stores no attack list.
        /// </summary>
        /// <remarks>
        ///     <c>Class89.anInt711</c>. <c>Node_Sub31_Sub2.java:741-745</c> reads it as an exponent:
        ///     0 steps the envelope by a fixed 128 per tick, anything else by
        ///     <c>128 * 2^(rate * k)</c>. -1 here means the field is absent from the file rather
        ///     than present and zero, which is a different number of bytes.
        /// </remarks>
        public int AttackRate { get; set; } = -1;

        /// <summary>
        ///     How fast the release envelope is walked, or -1 when there is no release list.
        /// </summary>
        /// <remarks><c>Class89.anInt715</c>, <c>Node_Sub31_Sub2.java:765-769</c>.</remarks>
        public int ReleaseRate { get; set; } = -1;

        /// <summary>
        ///     How fast the decay clock advances, or -1 when <see cref="Decay"/> is zero.
        /// </summary>
        /// <remarks><c>Class89.anInt712</c>, <c>Node_Sub31_Sub2.java:729-733</c>.</remarks>
        public int DecayRate { get; set; } = -1;

        /// <summary>How fast the vibrato oscillator turns, 0 for no vibrato.</summary>
        /// <remarks>
        ///     <c>Class89.anInt710</c>, added to the voice's phase every tick at
        ///     <c>Node_Sub31_Sub2.java:723</c> and read back as
        ///     <c>sin(phase &amp; 0x1ff)</c> at <c>:1090</c>.
        /// </remarks>
        public int VibratoRate { get; set; }

        /// <summary>
        ///     How far the vibrato bends the pitch, or -1 when <see cref="VibratoRate"/> is zero.
        /// </summary>
        /// <remarks><c>Class89.anInt708</c>, scaled by four at <c>Node_Sub31_Sub2.java:1081</c>.</remarks>
        public int VibratoDepth { get; set; } = -1;

        /// <summary>
        ///     How long the vibrato takes to reach full depth, or -1 when the depth is zero.
        /// </summary>
        /// <remarks>
        ///     <c>Class89.anInt717</c>. <c>Node_Sub31_Sub2.java:1084-1086</c> ramps the depth
        ///     linearly over twice this value, so a voice released before then never reaches it.
        /// </remarks>
        public int VibratoDelay { get; set; } = -1;

        /// <summary>Breakpoints the attack envelope declares.</summary>
        public int AttackPoints => AttackLevels.Length;

        /// <summary>
        ///     Breakpoints the release envelope declares.
        /// </summary>
        /// <remarks>
        ///     Taken from the time list rather than the level list because the level list is one
        ///     shorter by construction: the client presets the first level and leaves the last
        ///     unwritten, so a one-point release stores no level at all.
        /// </remarks>
        public int ReleasePoints => ReleaseTimeDeltas.Length;
    }
}
