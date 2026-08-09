using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Audio.Sfx2 {
    /// <summary>
    ///     One group of JS5 index 14, which holds two structurally unrelated things.
    /// </summary>
    /// <remarks>
    ///     Group 0 is the Vorbis setup header and codebooks every sample is decoded against;
    ///     every other group is one sample record. The split is stated by the client rather than
    ///     inferred: <c>Node_Sub13.method1133</c> (Node_Sub13.java:32) fetches
    ///     <c>getChildFromFolder(0, 0)</c> once and hands it to the setup parser
    ///     <c>method1143</c>, while <c>method1137</c> (:76) fetches a sample by its own id and hands
    ///     it to <c>method1142</c> (:494). Nothing ever passes group 0 to the sample reader.
    ///     <para>
    ///     Both caches agree with that: group 0's first four bytes read as a sample rate of
    ///     0xAA164243, which is negative, and its bytes 2..4 are the Vorbis codebook sync pattern.
    ///     It is not a sample that happens to be odd, it is a different format.
    ///     </para>
    ///     <para>
    ///     <b>This is a lossless codec, not a player.</b> The sample record's packets are carried
    ///     as the bytes the cache holds; nothing here turns them into PCM. That needs the client's
    ///     IMDCT and the codebook, floor, residue and mapping decoders
    ///     (<c>Node_Sub13.method1135</c>, <c>Class71</c>, <c>Class56</c>, <c>Class311</c>,
    ///     <c>Class371</c>), and it cannot be delegated to a stock Vorbis library because group 0 is
    ///     not a well-formed Vorbis setup packet - see <see cref="Sfx2SetupHeader"/>.
    ///     </para>
    /// </remarks>
    public abstract class Sfx2Entry {
        /// <summary>
        ///     The group id this entry was read from, which is also the sound-effect id.
        /// </summary>
        /// <remarks>
        ///     An external reference rather than a private label. <c>Class280.java:206</c> loads an
        ///     ambient sound with the raw id off its config record, and
        ///     <c>Particle_Sub3_Sub5_Sub2.java:99-100</c> hands this index to the MIDI synth
        ///     alongside indexes 15 and 4. Renumbering a group silently breaks whatever names it.
        /// </remarks>
        public int Id { get; set; } = -1;

        /// <summary>Writes this entry back to the bytes the group should store.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        public abstract JagStream Encode();

        /// <summary>
        ///     Reads whichever of the two record shapes a group holds.
        /// </summary>
        /// <remarks>
        ///     Dispatched on the group id because that is what the client dispatches on. The
        ///     content check is available separately as
        ///     <see cref="Sfx2SetupHeader.HasCodebookSyncPattern"/>, so a cache that disagreed with
        ///     the id rule would be caught by a sweep rather than papered over by a guess here.
        /// </remarks>
        /// <param name="groupId">The group the bytes came from.</param>
        /// <param name="stream">The group's single file, positioned at its start.</param>
        /// <returns>The decoded entry.</returns>
        /// <exception cref="ArgumentNullException">The stream is null.</exception>
        public static Sfx2Entry Decode(int groupId, JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            return groupId == Sfx2SetupHeader.SetupGroupId
                ? (Sfx2Entry) new Sfx2SetupHeader { Id = groupId }.Decode(stream)
                : new Sfx2Sample { Id = groupId }.Decode(stream);
        }
    }
}
