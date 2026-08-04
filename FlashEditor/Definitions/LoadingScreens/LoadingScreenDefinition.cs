using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.LoadingScreens {
    /// <summary>
    ///     One loading screen from group 1 of JS5 index 33: how long it shows and what is drawn on it.
    /// </summary>
    /// <remarks>
    ///     Decoded by <c>Class124.method2215</c> (Class124.java:96-111), reached through
    ///     <c>Class282.method3336</c> (Class282.java:169-180) as <c>getChildFromFolder(1, id)</c>.
    ///     <para>
    ///     <b>The group's file ids are not contiguous.</b> They are 0 and then a run starting well
    ///     above it, so a loop over <c>0..fileCount-1</c> reads files that do not exist and misses
    ///     the ones that do. Take them from the reference table entry.
    ///     </para>
    /// </remarks>
    public sealed class LoadingScreenDefinition {
        /// <summary>The group loading screens live in.</summary>
        public const int GroupId = 1;

        /// <summary>The screen id, which is its file id within group 1.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     How long the screen stays up, in milliseconds.
        /// </summary>
        /// <remarks>
        ///     A <b>unsigned</b> 24-bit field - <c>RSBuffer.method1186</c> (RSBuffer.java:131-135),
        ///     which does not sign-extend, unlike the signed reader element type 6 uses. Proven to be
        ///     milliseconds by Class210.java:147, which compares it against
        ///     <c>System.currentTimeMillis</c>.
        /// </remarks>
        public int DisplayDurationMs { get; set; }

        /// <summary>
        ///     The second timing field, whose role the 637 client does not settle.
        /// </summary>
        /// <remarks>
        ///     <c>Class210.method25</c> returns it and nothing else reads it, so it is named after
        ///     where it sits rather than after a guess.
        /// </remarks>
        public int SecondTiming { get; set; }

        /// <summary>The drawables, in the order the file stores them.</summary>
        /// <remarks>
        ///     Order is the format, not a presentation choice: the client draws them in this order,
        ///     so it is also the z-order.
        /// </remarks>
        public List<LoadingScreenElement> Elements { get; } = new List<LoadingScreenElement>();

        /// <summary>Reads one screen from its file.</summary>
        /// <param name="stream">The file, positioned at its first byte.</param>
        /// <returns>This definition.</returns>
        public LoadingScreenDefinition Decode(JagStream stream) {
            Elements.Clear();

            DisplayDurationMs = stream.ReadMedium();
            SecondTiming = stream.ReadUnsignedShort();

            int count = stream.ReadUnsignedByte();
            for (int i = 0; i < count; i++) {
                LoadingScreenElement element = LoadingScreenElement.Create(stream.ReadUnsignedByte());
                element.Decode(stream);
                Elements.Add(element);
            }

            return this;
        }

        /// <summary>Writes this screen back to the file representation.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            if (Elements.Count > byte.MaxValue)
                throw new InvalidOperationException(
                    "A loading screen's element count is a single byte, so it cannot hold " +
                    Elements.Count + " elements.");

            var stream = new JagStream();
            stream.WriteMedium(DisplayDurationMs);
            stream.WriteShort(SecondTiming);
            stream.WriteByte((byte) Elements.Count);

            foreach (LoadingScreenElement element in Elements) {
                stream.WriteByte((byte) element.TypeIndex);
                element.Encode(stream);
            }

            return stream.Flip();
        }

        /// <summary>Takes a copy no edit through this instance can reach.</summary>
        /// <returns>An independent definition holding the same values.</returns>
        public LoadingScreenDefinition Clone() {
            var copy = new LoadingScreenDefinition {
                Id = Id,
                DisplayDurationMs = DisplayDurationMs,
                SecondTiming = SecondTiming
            };

            foreach (LoadingScreenElement element in Elements)
                copy.Elements.Add(element.Clone());

            return copy;
        }
    }
}
