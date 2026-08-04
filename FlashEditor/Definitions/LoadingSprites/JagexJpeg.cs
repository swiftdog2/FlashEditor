using System;
using System.Collections.Generic;
using System.IO;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>One component declared by a frame header.</summary>
    /// <param name="Id">The component identifier the scan header refers to it by.</param>
    /// <param name="HorizontalSampling">Horizontal sampling factor.</param>
    /// <param name="VerticalSampling">Vertical sampling factor.</param>
    /// <param name="QuantisationTableId">Which <c>DQT</c> table dequantises it.</param>
    public readonly record struct JpegComponent(int Id, int HorizontalSampling, int VerticalSampling,
        int QuantisationTableId);

    /// <summary>One component as a scan header selects it.</summary>
    /// <param name="ComponentId">The frame component this entry selects.</param>
    /// <param name="DcTableId">Which <c>DHT</c> table decodes its DC coefficients.</param>
    /// <param name="AcTableId">Which <c>DHT</c> table decodes its AC coefficients.</param>
    public readonly record struct JpegScanComponent(int ComponentId, int DcTableId, int AcTableId);

    /// <summary>One Huffman table, as a <c>DHT</c> segment states it.</summary>
    /// <remarks>
    ///     Held as the counts and symbols the file carries rather than as a decoded code table, so
    ///     it can be compared against another file's byte for byte. All twenty-one index-32 images
    ///     and the client's own probe blob carry the same four.
    /// </remarks>
    public sealed class JpegHuffmanTable {
        /// <summary>How many codes there are of each length, 1 to 16.</summary>
        public byte[] Counts { get; }

        /// <summary>The symbols, in code order.</summary>
        public byte[] Symbols { get; }

        /// <summary>Binds a table's counts to its symbols.</summary>
        /// <param name="counts">Sixteen code-length counts.</param>
        /// <param name="symbols">The symbols, one per code.</param>
        public JpegHuffmanTable(byte[] counts, byte[] symbols) {
            Counts = counts ?? throw new ArgumentNullException(nameof(counts));
            Symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        }
    }

    /// <summary>
    ///     The structure of one of index 32's JPEG images, parsed so that every stored byte is
    ///     accounted for and can be written back unchanged.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This type never becomes the source of truth for the file's bytes.</b> A JPEG re-encode
    ///     is no more reproducible than a GZip one - the entropy coder, the quantisation and the
    ///     forward DCT are all implementation choices - so the only way an index-32 image survives a
    ///     save byte for byte is for the stored bytes to be kept and written back. That is what
    ///     <see cref="LoadingSpriteDefinition.Encode"/> does. What this parse buys is the ability to
    ///     say what the file <i>is</i>, and <see cref="ToBytes"/> exists so a sweep can prove the
    ///     parse accounted for every byte rather than that being assumed.
    ///     </para>
    ///     <para>
    ///     <b>The client settles nothing about the pixels.</b> <c>Class271.method3277</c>
    ///     (<c>Class271.java:29-65</c>) hands the bytes straight to
    ///     <c>Toolkit.getDefaultToolkit().createImage(byte[])</c> and grabs the result with a
    ///     <c>PixelGrabber</c>; there is no colour transform, no component handling and no format
    ///     knowledge anywhere in the 637 source. The colour model is therefore established from the
    ///     file's own tables instead - see <see cref="BaselineJpegDecoder"/>.
    ///     </para>
    ///     <para>
    ///     <b>Every one of these files is the same shape.</b> Baseline sequential (<c>SOF0</c>), no
    ///     <c>JFIF APP0</c>, no <c>Adobe APP14</c>, no restart interval, one scan, four components
    ///     sampled 2x2, 1x1, 1x1 and 2x2. The client ships a 1x1 image of exactly that shape as a
    ///     capability probe - <c>Class74.aByteArray546</c>, gunzipped and decoded by
    ///     <c>Class116.method2162</c> (<c>Class116.java:60-77</c>) - and falls back to index 34 when
    ///     the JVM cannot decode it. Its two quantisation tables and four Huffman tables are
    ///     byte-identical to the ones in all twenty-one cache images, which is what ties the shape to
    ///     the client rather than to an inference about it.
    ///     </para>
    /// </remarks>
    public sealed class JagexJpeg {
        /// <summary>Start of image.</summary>
        public const byte MarkerSoi = 0xD8;

        /// <summary>End of image.</summary>
        public const byte MarkerEoi = 0xD9;

        /// <summary>Start of scan.</summary>
        public const byte MarkerSos = 0xDA;

        /// <summary>Define quantisation table.</summary>
        public const byte MarkerDqt = 0xDB;

        /// <summary>Define Huffman table.</summary>
        public const byte MarkerDht = 0xC4;

        /// <summary>Define restart interval.</summary>
        public const byte MarkerDri = 0xDD;

        /// <summary>Baseline sequential DCT frame header - the only frame type this cache uses.</summary>
        public const byte MarkerSof0 = 0xC0;

        /// <summary>Coefficients in one block, and entries in one quantisation table.</summary>
        internal const int BlockCoefficients = 64;

        /// <summary>
        ///     The zig-zag order a block's coefficients are stored in.
        /// </summary>
        /// <remarks>
        ///     <c>ZigZag[k]</c> is the natural-order position of the <c>k</c>th stored coefficient,
        ///     so both the dequantiser and the quantisation-table reader index through it the same
        ///     way. Reading a table without it produces a plausible image with the wrong detail.
        /// </remarks>
        internal static readonly int[] ZigZag = {
             0,  1,  8, 16,  9,  2,  3, 10, 17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        };

        private readonly List<JpegSegment> _segments = new List<JpegSegment>();
        private readonly List<JpegComponent> _components = new List<JpegComponent>();
        private readonly List<JpegScanComponent> _scanComponents = new List<JpegScanComponent>();
        private readonly Dictionary<int, int[]> _quantisation = new Dictionary<int, int[]>();
        private readonly Dictionary<int, JpegHuffmanTable> _dcTables = new Dictionary<int, JpegHuffmanTable>();
        private readonly Dictionary<int, JpegHuffmanTable> _acTables = new Dictionary<int, JpegHuffmanTable>();

        /// <summary>Every marker segment, in file order, up to and including the scan header.</summary>
        public IReadOnlyList<JpegSegment> Segments => _segments;

        /// <summary>
        ///     The entropy-coded bytes, exactly as stored - byte stuffing intact.
        /// </summary>
        /// <remarks>
        ///     Kept stuffed rather than unstuffed so that <see cref="ToBytes"/> is a straight
        ///     concatenation. Undoing the <c>FF 00</c> stuffing is the entropy reader's job.
        /// </remarks>
        public byte[] EntropyCodedData { get; private set; } = Array.Empty<byte>();

        /// <summary>Whatever follows the entropy-coded data, normally the two <c>FF D9</c> bytes.</summary>
        public byte[] Trailer { get; private set; } = Array.Empty<byte>();

        /// <summary>Image width in pixels, from the frame header.</summary>
        public int Width { get; private set; }

        /// <summary>Image height in pixels, from the frame header.</summary>
        public int Height { get; private set; }

        /// <summary>Sample precision in bits, from the frame header.</summary>
        public int Precision { get; private set; }

        /// <summary>Which frame header the file carries, which decides whether it can be rendered.</summary>
        public byte FrameMarker { get; private set; }

        /// <summary>Whether the file is baseline sequential, the only kind this decoder renders.</summary>
        public bool IsBaseline => FrameMarker == MarkerSof0;

        /// <summary>The frame's components, in the order the frame header declares them.</summary>
        public IReadOnlyList<JpegComponent> Components => _components;

        /// <summary>The scan's component selectors, in scan order.</summary>
        public IReadOnlyList<JpegScanComponent> ScanComponents => _scanComponents;

        /// <summary>Quantisation tables by id, each 64 entries in natural order.</summary>
        public IReadOnlyDictionary<int, int[]> QuantisationTables => _quantisation;

        /// <summary>DC Huffman tables by id.</summary>
        public IReadOnlyDictionary<int, JpegHuffmanTable> DcTables => _dcTables;

        /// <summary>AC Huffman tables by id.</summary>
        public IReadOnlyDictionary<int, JpegHuffmanTable> AcTables => _acTables;

        /// <summary>
        ///     MCUs between restart markers, or 0 when the file declares no restart interval.
        /// </summary>
        /// <remarks>No index-32 image carries a <c>DRI</c> segment, so this is 0 throughout.</remarks>
        public int RestartInterval { get; private set; }

        /// <summary>
        ///     Parses a JPEG file into its segments, its scan and its trailer.
        /// </summary>
        /// <remarks>
        ///     Strict rather than forgiving on purpose. A segment whose declared length does not
        ///     match the bytes that follow it, a fill byte between markers, or anything else this
        ///     rejects would have to be dropped or guessed at to carry on, and a parse that drops a
        ///     byte cannot be held against the stored file afterwards.
        /// </remarks>
        /// <param name="bytes">The whole file.</param>
        /// <returns>The parsed structure.</returns>
        /// <exception cref="InvalidDataException">The file is not a JPEG this parse can account for.</exception>
        public static JagexJpeg Decode(byte[] bytes) {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != MarkerSoi)
                throw new InvalidDataException("A JPEG has to open with the SOI marker FF D8.");

            var jpeg = new JagexJpeg();
            int at = 0;

            while (true) {
                if (at + 2 > bytes.Length)
                    throw new InvalidDataException($"The file ends at {at} where a marker was expected.");
                if (bytes[at] != 0xFF)
                    throw new InvalidDataException(
                        $"Expected a marker at {at} but found 0x{bytes[at]:X2}. Fill bytes and stray data " +
                        "between segments are rejected rather than skipped, because skipping one loses it.");

                byte marker = bytes[at + 1];
                if (marker == 0xFF)
                    throw new InvalidDataException($"A fill byte at {at + 1} would be lost on the way out.");

                if (JpegSegment.IsStandalone(marker)) {
                    jpeg._segments.Add(new JpegSegment(marker, null));
                    at += 2;
                    if (marker == MarkerEoi)
                        break;
                    continue;
                }

                if (at + 4 > bytes.Length)
                    throw new InvalidDataException($"Segment FF{marker:X2} at {at} has no length field.");

                int declared = (bytes[at + 2] << 8) | bytes[at + 3];
                if (declared < 2 || at + 2 + declared > bytes.Length)
                    throw new InvalidDataException(
                        $"Segment FF{marker:X2} at {at} declares {declared} bytes, which does not fit the file.");

                byte[] payload = new byte[declared - 2];
                Array.Copy(bytes, at + 4, payload, 0, payload.Length);
                jpeg._segments.Add(new JpegSegment(marker, payload));
                jpeg.Interpret(marker, payload);
                at += 2 + declared;

                if (marker != MarkerSos)
                    continue;

                //The scan runs to the next marker that is neither a stuffed FF 00 nor a restart.
                int end = FindScanEnd(bytes, at);
                jpeg.EntropyCodedData = new byte[end - at];
                Array.Copy(bytes, at, jpeg.EntropyCodedData, 0, jpeg.EntropyCodedData.Length);
                jpeg.Trailer = new byte[bytes.Length - end];
                Array.Copy(bytes, end, jpeg.Trailer, 0, jpeg.Trailer.Length);
                break;
            }

            if (jpeg.FrameMarker == 0)
                throw new InvalidDataException("The file carries no frame header, so it declares no image.");

            return jpeg;
        }

        /// <summary>
        ///     Rebuilds the file from the parsed parts.
        /// </summary>
        /// <remarks>
        ///     Not the save path - <see cref="LoadingSpriteDefinition.Encode"/> writes the stored
        ///     bytes instead. This exists so a sweep can hold the parse against the file it came
        ///     from: a segment sized wrongly, a scan that ended in the wrong place or a dropped
        ///     trailer all show up as a byte difference here and nowhere else.
        /// </remarks>
        /// <returns>The reassembled file.</returns>
        public byte[] ToBytes() {
            int length = Trailer.Length + EntropyCodedData.Length;
            foreach (JpegSegment segment in _segments)
                length += segment.StoredLength;

            byte[] output = new byte[length];
            int at = 0;
            foreach (JpegSegment segment in _segments) {
                output[at++] = 0xFF;
                output[at++] = segment.Marker;
                if (segment.HasPayload) {
                    int declared = segment.Payload.Length + 2;
                    output[at++] = (byte) (declared >> 8);
                    output[at++] = (byte) declared;
                    Array.Copy(segment.Payload, 0, output, at, segment.Payload.Length);
                    at += segment.Payload.Length;
                }

                if (segment.Marker != MarkerSos)
                    continue;

                Array.Copy(EntropyCodedData, 0, output, at, EntropyCodedData.Length);
                at += EntropyCodedData.Length;
                Array.Copy(Trailer, 0, output, at, Trailer.Length);
                at += Trailer.Length;
            }

            return output;
        }

        /// <summary>
        ///     Finds where the entropy-coded data stops.
        /// </summary>
        /// <remarks>
        ///     Inside the scan a <c>0xFF</c> is either stuffed - followed by <c>0x00</c> so it
        ///     cannot be read as a marker - or a restart marker, which belongs to the scan. Anything
        ///     else ends it.
        /// </remarks>
        /// <param name="bytes">The whole file.</param>
        /// <param name="from">Where the scan starts.</param>
        /// <returns>The offset just past the last entropy-coded byte.</returns>
        private static int FindScanEnd(byte[] bytes, int from) {
            for (int at = from; at < bytes.Length - 1; at++) {
                if (bytes[at] != 0xFF)
                    continue;
                byte next = bytes[at + 1];
                if (next == 0x00 || next == 0xFF || JpegSegment.IsRestart(next))
                    continue;
                return at;
            }

            return bytes.Length;
        }

        /// <summary>Reads the tables and headers a segment carries.</summary>
        /// <param name="marker">The segment's marker.</param>
        /// <param name="payload">Its body.</param>
        private void Interpret(byte marker, byte[] payload) {
            switch (marker) {
                case MarkerDqt:
                    ReadQuantisationTables(payload);
                    return;
                case MarkerDht:
                    ReadHuffmanTables(payload);
                    return;
                case MarkerDri:
                    if (payload.Length < 2)
                        throw new InvalidDataException("A DRI segment carries two bytes.");
                    RestartInterval = (payload[0] << 8) | payload[1];
                    return;
                case MarkerSos:
                    ReadScanHeader(payload);
                    return;
            }

            //Every SOFn bar the four that are not frame headers at all.
            if (marker >= 0xC0 && marker <= 0xCF && marker != MarkerDht && marker != 0xC8 && marker != 0xCC)
                ReadFrameHeader(marker, payload);
        }

        /// <summary>Reads however many quantisation tables one <c>DQT</c> segment holds.</summary>
        /// <param name="payload">The segment body.</param>
        private void ReadQuantisationTables(byte[] payload) {
            int at = 0;
            while (at < payload.Length) {
                int precision = payload[at] >> 4;
                int id = payload[at] & 0x0F;
                at++;

                int[] table = new int[BlockCoefficients];
                for (int k = 0; k < BlockCoefficients; k++) {
                    if (precision == 0) {
                        if (at >= payload.Length)
                            throw new InvalidDataException("A DQT segment ends inside a table.");
                        table[ZigZag[k]] = payload[at++];
                    } else {
                        if (at + 1 >= payload.Length)
                            throw new InvalidDataException("A DQT segment ends inside a 16-bit table.");
                        table[ZigZag[k]] = (payload[at] << 8) | payload[at + 1];
                        at += 2;
                    }
                }

                _quantisation[id] = table;
            }
        }

        /// <summary>Reads however many Huffman tables one <c>DHT</c> segment holds.</summary>
        /// <param name="payload">The segment body.</param>
        private void ReadHuffmanTables(byte[] payload) {
            int at = 0;
            while (at < payload.Length) {
                int cls = payload[at] >> 4;
                int id = payload[at] & 0x0F;
                at++;

                if (at + 16 > payload.Length)
                    throw new InvalidDataException("A DHT segment ends inside its code-length counts.");

                byte[] counts = new byte[16];
                int total = 0;
                for (int i = 0; i < 16; i++) {
                    counts[i] = payload[at + i];
                    total += counts[i];
                }
                at += 16;

                if (at + total > payload.Length)
                    throw new InvalidDataException("A DHT segment ends inside its symbol list.");

                byte[] symbols = new byte[total];
                Array.Copy(payload, at, symbols, 0, total);
                at += total;

                var table = new JpegHuffmanTable(counts, symbols);
                if (cls == 0)
                    _dcTables[id] = table;
                else
                    _acTables[id] = table;
            }
        }

        /// <summary>Reads a frame header.</summary>
        /// <param name="marker">Which <c>SOFn</c> it is.</param>
        /// <param name="payload">The segment body.</param>
        private void ReadFrameHeader(byte marker, byte[] payload) {
            if (payload.Length < 6)
                throw new InvalidDataException("A frame header is at least six bytes.");

            FrameMarker = marker;
            Precision = payload[0];
            Height = (payload[1] << 8) | payload[2];
            Width = (payload[3] << 8) | payload[4];

            int count = payload[5];
            if (payload.Length < 6 + count * 3)
                throw new InvalidDataException($"A frame header declaring {count} components is longer than this.");

            _components.Clear();
            for (int i = 0; i < count; i++) {
                int at = 6 + i * 3;
                _components.Add(new JpegComponent(payload[at], payload[at + 1] >> 4, payload[at + 1] & 0x0F,
                    payload[at + 2]));
            }
        }

        /// <summary>Reads a scan header.</summary>
        /// <param name="payload">The segment body.</param>
        private void ReadScanHeader(byte[] payload) {
            if (payload.Length < 1)
                throw new InvalidDataException("A scan header is at least one byte.");

            int count = payload[0];
            if (payload.Length < 1 + count * 2 + 3)
                throw new InvalidDataException($"A scan header selecting {count} components is longer than this.");

            _scanComponents.Clear();
            for (int i = 0; i < count; i++) {
                int at = 1 + i * 2;
                _scanComponents.Add(new JpegScanComponent(payload[at], payload[at + 1] >> 4, payload[at + 1] & 0x0F));
            }
        }
    }
}
