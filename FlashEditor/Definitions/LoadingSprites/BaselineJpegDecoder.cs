using System;
using System.Collections.Generic;
using System.IO;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     Decodes a baseline sequential JPEG into full-resolution component planes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Written rather than delegated to <c>System.Drawing</c> for a specific reason. These files
    ///     carry four components and neither a <c>JFIF APP0</c> nor an <c>Adobe APP14</c> marker, so
    ///     every general-purpose decoder falls back to CMYK and produces a plausible but wrong
    ///     picture. <c>Image.FromStream</c> opens one happily as <c>Format32bppCMYK</c>; the raw
    ///     component planes it took to get there - which are what the colour model has to be applied
    ///     to - are gone by the time a caller sees a pixel.
    ///     </para>
    ///     <para>
    ///     The pipeline is libjpeg's, step for step, because libjpeg is what the client's JVM runs
    ///     under <c>Toolkit.createImage</c> (<c>Class271.java:29-65</c>): the same Huffman decode,
    ///     the same dequantisation, and the same <c>h2v2</c> triangle chroma upsampling
    ///     (<c>jdsample.c</c>'s <c>h2v2_fancy_upsample</c>) rather than nearest-neighbour
    ///     replication. Only the inverse DCT differs - this uses a straight double-precision
    ///     separable transform where libjpeg uses a scaled integer one - which puts the two within
    ///     one sample of each other, measured over every image in the index.
    ///     </para>
    ///     <para>
    ///     Baseline only. Every index-32 image and the client's own probe blob are <c>SOF0</c>, and
    ///     a progressive file needs a wholly different scan structure, so it is refused rather than
    ///     half-decoded.
    ///     </para>
    /// </remarks>
    public static class BaselineJpegDecoder {
        /// <summary>Samples along one side of a block.</summary>
        private const int BlockSize = 8;

        /// <summary>
        ///     The 1-D cosine basis, with the DC term's <c>1/sqrt(2)</c> already folded in.
        /// </summary>
        private static readonly double[,] Cosines = BuildCosines();

        /// <summary>
        ///     Decodes an image into one full-resolution plane per component.
        /// </summary>
        /// <param name="jpeg">The parsed file.</param>
        /// <returns>The decoded planes.</returns>
        /// <exception cref="InvalidDataException">The file is not a baseline JPEG this can decode.</exception>
        public static JpegRaster Decode(JagexJpeg jpeg) {
            if (jpeg == null)
                throw new ArgumentNullException(nameof(jpeg));
            if (!jpeg.IsBaseline)
                throw new InvalidDataException(
                    $"Only baseline sequential JPEG is decoded here; this file's frame header is " +
                    $"FF{jpeg.FrameMarker:X2}.");
            if (jpeg.Precision != 8)
                throw new InvalidDataException($"An {jpeg.Precision}-bit sample precision is not supported.");
            if (jpeg.Components.Count == 0)
                throw new InvalidDataException("The frame declares no components.");
            if (jpeg.Width <= 0 || jpeg.Height <= 0)
                throw new InvalidDataException($"The frame declares a {jpeg.Width}x{jpeg.Height} image.");

            int maxH = 1;
            int maxV = 1;
            foreach (JpegComponent component in jpeg.Components) {
                if (component.HorizontalSampling < 1 || component.VerticalSampling < 1)
                    throw new InvalidDataException($"Component {component.Id} declares a zero sampling factor.");
                maxH = Math.Max(maxH, component.HorizontalSampling);
                maxV = Math.Max(maxV, component.VerticalSampling);
            }

            int mcusAcross = Ceil(jpeg.Width, BlockSize * maxH);
            int mcusDown = Ceil(jpeg.Height, BlockSize * maxV);

            var planes = new byte[jpeg.Components.Count][];
            var planeWidths = new int[jpeg.Components.Count];
            for (int i = 0; i < jpeg.Components.Count; i++) {
                JpegComponent component = jpeg.Components[i];
                planeWidths[i] = mcusAcross * component.HorizontalSampling * BlockSize;
                planes[i] = new byte[planeWidths[i] * mcusDown * component.VerticalSampling * BlockSize];
            }

            int consumed = DecodeScan(jpeg, planes, planeWidths, mcusAcross, mcusDown);

            var full = new byte[jpeg.Components.Count][];
            for (int i = 0; i < jpeg.Components.Count; i++) {
                JpegComponent component = jpeg.Components[i];
                int scaleX = maxH / component.HorizontalSampling;
                int scaleY = maxV / component.VerticalSampling;
                if (scaleX * component.HorizontalSampling != maxH || scaleY * component.VerticalSampling != maxV)
                    throw new InvalidDataException(
                        $"Component {component.Id} is sampled {component.HorizontalSampling}x" +
                        $"{component.VerticalSampling} against a maximum of {maxH}x{maxV}, which is not a whole " +
                        "upsampling ratio.");

                //Crop to the component's own resolution first. libjpeg's upsampler works from that
                //rather than from the block-padded plane, and its edge cases read the first and
                //last real column, so upsampling the padding shifts the whole right-hand edge.
                int componentWidth = Ceil(jpeg.Width * component.HorizontalSampling, maxH);
                int componentHeight = Ceil(jpeg.Height * component.VerticalSampling, maxV);
                byte[] cropped = Crop(planes[i], planeWidths[i], componentWidth, componentHeight);

                full[i] = Upsample(cropped, componentWidth, componentHeight, scaleX, scaleY,
                    jpeg.Width, jpeg.Height);
            }

            return new JpegRaster(jpeg.Width, jpeg.Height, full, consumed, jpeg.EntropyCodedData.Length);
        }

        /// <summary>
        ///     Walks the entropy-coded scan, filling one block at a time.
        /// </summary>
        /// <param name="jpeg">The parsed file.</param>
        /// <param name="planes">The block-padded destination planes.</param>
        /// <param name="planeWidths">Each padded plane's row stride.</param>
        /// <param name="mcusAcross">MCUs per row.</param>
        /// <param name="mcusDown">MCU rows.</param>
        /// <returns>How many scan bytes were read.</returns>
        private static int DecodeScan(JagexJpeg jpeg, byte[][] planes, int[] planeWidths, int mcusAcross,
            int mcusDown) {
            var order = new List<int>(jpeg.ScanComponents.Count);
            var dcTables = new List<HuffmanCodes>(jpeg.ScanComponents.Count);
            var acTables = new List<HuffmanCodes>(jpeg.ScanComponents.Count);

            foreach (JpegScanComponent selector in jpeg.ScanComponents) {
                int index = -1;
                for (int i = 0; i < jpeg.Components.Count; i++)
                    if (jpeg.Components[i].Id == selector.ComponentId)
                        index = i;

                if (index < 0)
                    throw new InvalidDataException(
                        $"The scan selects component {selector.ComponentId}, which the frame does not declare.");
                if (!jpeg.DcTables.TryGetValue(selector.DcTableId, out JpegHuffmanTable dc))
                    throw new InvalidDataException($"The scan names DC table {selector.DcTableId}, which is absent.");
                if (!jpeg.AcTables.TryGetValue(selector.AcTableId, out JpegHuffmanTable ac))
                    throw new InvalidDataException($"The scan names AC table {selector.AcTableId}, which is absent.");

                order.Add(index);
                dcTables.Add(new HuffmanCodes(dc));
                acTables.Add(new HuffmanCodes(ac));
            }

            if (order.Count != jpeg.Components.Count)
                throw new InvalidDataException(
                    $"The scan selects {order.Count} of the frame's {jpeg.Components.Count} components, so it is " +
                    "not the single interleaved scan a baseline file of this shape carries.");

            var reader = new EntropyReader(jpeg.EntropyCodedData);
            var predictors = new int[jpeg.Components.Count];
            var coefficients = new int[JagexJpeg.BlockCoefficients];
            var samples = new byte[JagexJpeg.BlockCoefficients];
            int sinceRestart = 0;

            for (int mcuY = 0; mcuY < mcusDown; mcuY++) {
                for (int mcuX = 0; mcuX < mcusAcross; mcuX++) {
                    if (jpeg.RestartInterval > 0 && sinceRestart == jpeg.RestartInterval) {
                        reader.SyncToRestart();
                        Array.Clear(predictors, 0, predictors.Length);
                        sinceRestart = 0;
                    }
                    sinceRestart++;

                    for (int s = 0; s < order.Count; s++) {
                        int index = order[s];
                        JpegComponent component = jpeg.Components[index];
                        if (!jpeg.QuantisationTables.TryGetValue(component.QuantisationTableId, out int[] quant))
                            throw new InvalidDataException(
                                $"Component {component.Id} names quantisation table {component.QuantisationTableId}," +
                                " which is absent.");

                        for (int blockY = 0; blockY < component.VerticalSampling; blockY++) {
                            for (int blockX = 0; blockX < component.HorizontalSampling; blockX++) {
                                ReadBlock(reader, dcTables[s], acTables[s], quant, ref predictors[index],
                                    coefficients);
                                InverseDct(coefficients, samples);

                                int originX = (mcuX * component.HorizontalSampling + blockX) * BlockSize;
                                int originY = (mcuY * component.VerticalSampling + blockY) * BlockSize;
                                byte[] plane = planes[index];
                                int stride = planeWidths[index];
                                for (int row = 0; row < BlockSize; row++) {
                                    Array.Copy(samples, row * BlockSize, plane,
                                        (originY + row) * stride + originX, BlockSize);
                                }
                            }
                        }
                    }
                }
            }

            return reader.BytesConsumed;
        }

        /// <summary>
        ///     Reads one block's dequantised coefficients in natural order.
        /// </summary>
        /// <param name="reader">The entropy reader.</param>
        /// <param name="dc">The component's DC table.</param>
        /// <param name="ac">The component's AC table.</param>
        /// <param name="quant">The component's quantisation table, natural order.</param>
        /// <param name="predictor">The running DC predictor for this component.</param>
        /// <param name="coefficients">The 64-entry destination, cleared here.</param>
        private static void ReadBlock(EntropyReader reader, HuffmanCodes dc, HuffmanCodes ac, int[] quant,
            ref int predictor, int[] coefficients) {
            Array.Clear(coefficients, 0, coefficients.Length);

            int magnitude = reader.DecodeSymbol(dc);
            predictor += Extend(reader.Receive(magnitude), magnitude);
            coefficients[0] = predictor * quant[0];

            int k = 1;
            while (k < JagexJpeg.BlockCoefficients) {
                int runSize = reader.DecodeSymbol(ac);
                int run = runSize >> 4;
                int size = runSize & 0x0F;

                if (size == 0) {
                    //A run of 15 with no magnitude is the sixteen-zero escape; anything else is
                    //end of block.
                    if (run != 15)
                        break;
                    k += 16;
                    continue;
                }

                k += run;
                if (k >= JagexJpeg.BlockCoefficients)
                    throw new InvalidDataException($"A coefficient run reached {k}, past the end of the block.");

                int position = JagexJpeg.ZigZag[k];
                coefficients[position] = Extend(reader.Receive(size), size) * quant[position];
                k++;
            }
        }

        /// <summary>
        ///     Sign-extends an n-bit magnitude, the specification's <c>EXTEND</c>.
        /// </summary>
        /// <param name="value">The raw bits.</param>
        /// <param name="bits">How many were read.</param>
        /// <returns>The signed coefficient.</returns>
        private static int Extend(int value, int bits) {
            if (bits == 0)
                return 0;
            return value >= (1 << (bits - 1)) ? value : value - (1 << bits) + 1;
        }

        /// <summary>
        ///     The separable inverse DCT, with the level shift and clamping the specification asks for.
        /// </summary>
        /// <param name="coefficients">Dequantised coefficients in natural order.</param>
        /// <param name="samples">The 64 destination samples.</param>
        private static void InverseDct(int[] coefficients, byte[] samples) {
            Span<double> rows = stackalloc double[JagexJpeg.BlockCoefficients];

            for (int y = 0; y < BlockSize; y++) {
                for (int x = 0; x < BlockSize; x++) {
                    double sum = 0.0;
                    for (int u = 0; u < BlockSize; u++)
                        sum += Cosines[x, u] * coefficients[y * BlockSize + u];
                    rows[y * BlockSize + x] = sum;
                }
            }

            for (int x = 0; x < BlockSize; x++) {
                for (int y = 0; y < BlockSize; y++) {
                    double sum = 0.0;
                    for (int v = 0; v < BlockSize; v++)
                        sum += Cosines[y, v] * rows[v * BlockSize + x];

                    int value = (int) Math.Floor(sum / 4.0 + 128.5);
                    samples[y * BlockSize + x] = (byte) (value < 0 ? 0 : (value > 255 ? 255 : value));
                }
            }
        }

        /// <summary>Copies the top-left of a block-padded plane.</summary>
        /// <param name="plane">The padded plane.</param>
        /// <param name="stride">Its row stride.</param>
        /// <param name="width">Wanted width.</param>
        /// <param name="height">Wanted height.</param>
        /// <returns>The cropped plane.</returns>
        private static byte[] Crop(byte[] plane, int stride, int width, int height) {
            byte[] output = new byte[width * height];
            for (int y = 0; y < height; y++)
                Array.Copy(plane, y * stride, output, y * width, width);
            return output;
        }

        /// <summary>
        ///     Grows a component plane to the image's own resolution.
        /// </summary>
        /// <remarks>
        ///     The 2x2 case gets libjpeg's triangle filter rather than sample replication, because
        ///     that is what the JVM's decoder does by default and the two differ by up to about
        ///     thirty on a chroma edge. Every other ratio falls back to replication, which no file
        ///     in this index reaches.
        /// </remarks>
        /// <param name="plane">The component plane at its own resolution.</param>
        /// <param name="width">Its width.</param>
        /// <param name="height">Its height.</param>
        /// <param name="scaleX">Horizontal upsampling ratio.</param>
        /// <param name="scaleY">Vertical upsampling ratio.</param>
        /// <param name="targetWidth">The image width.</param>
        /// <param name="targetHeight">The image height.</param>
        /// <returns>The full-resolution plane.</returns>
        private static byte[] Upsample(byte[] plane, int width, int height, int scaleX, int scaleY,
            int targetWidth, int targetHeight) {
            if (scaleX == 1 && scaleY == 1)
                return Crop(plane, width, targetWidth, targetHeight);

            byte[] grown = scaleX == 2 && scaleY == 2
                ? FancyUpsampleTwice(plane, width, height)
                : Replicate(plane, width, height, scaleX, scaleY);

            return Crop(grown, width * scaleX, targetWidth, targetHeight);
        }

        /// <summary>Nearest-neighbour upsampling, for a ratio the triangle filter does not cover.</summary>
        /// <param name="plane">The component plane.</param>
        /// <param name="width">Its width.</param>
        /// <param name="height">Its height.</param>
        /// <param name="scaleX">Horizontal ratio.</param>
        /// <param name="scaleY">Vertical ratio.</param>
        /// <returns>The grown plane.</returns>
        private static byte[] Replicate(byte[] plane, int width, int height, int scaleX, int scaleY) {
            int outWidth = width * scaleX;
            byte[] output = new byte[outWidth * height * scaleY];
            for (int y = 0; y < height * scaleY; y++) {
                int source = (y / scaleY) * width;
                int destination = y * outWidth;
                for (int x = 0; x < outWidth; x++)
                    output[destination + x] = plane[source + x / scaleX];
            }
            return output;
        }

        /// <summary>
        ///     libjpeg's <c>h2v2_fancy_upsample</c>: a 3/4 to 1/4 triangle filter in both axes.
        /// </summary>
        /// <remarks>
        ///     Each output sample is a weighted blend of the nearest input sample and its neighbour
        ///     in each direction, 3:1 horizontally and 3:1 vertically, which is the 9/3/3/1 kernel
        ///     the shifts below spell out. At the edges the neighbour row and column are the nearest
        ///     ones duplicated, matching libjpeg's context-row handling.
        /// </remarks>
        /// <param name="plane">The half-resolution plane.</param>
        /// <param name="width">Its width.</param>
        /// <param name="height">Its height.</param>
        /// <returns>The doubled plane, <c>2 * width</c> by <c>2 * height</c>.</returns>
        private static byte[] FancyUpsampleTwice(byte[] plane, int width, int height) {
            int outWidth = width * 2;
            byte[] output = new byte[outWidth * height * 2];
            int[] columns = new int[width];

            for (int outputRow = 0; outputRow < height * 2; outputRow++) {
                int inputRow = outputRow >> 1;
                int neighbourRow = (outputRow & 1) == 0
                    ? Math.Max(inputRow - 1, 0)
                    : Math.Min(inputRow + 1, height - 1);

                for (int x = 0; x < width; x++)
                    columns[x] = plane[inputRow * width + x] * 3 + plane[neighbourRow * width + x];

                int destination = outputRow * outWidth;
                if (width == 1) {
                    output[destination] = (byte) ((columns[0] * 4 + 8) >> 4);
                    output[destination + 1] = (byte) ((columns[0] * 4 + 7) >> 4);
                    continue;
                }

                output[destination] = (byte) ((columns[0] * 4 + 8) >> 4);
                output[destination + 1] = (byte) ((columns[0] * 3 + columns[1] + 7) >> 4);

                for (int x = 1; x < width - 1; x++) {
                    output[destination + 2 * x] = (byte) ((columns[x] * 3 + columns[x - 1] + 8) >> 4);
                    output[destination + 2 * x + 1] = (byte) ((columns[x] * 3 + columns[x + 1] + 7) >> 4);
                }

                output[destination + outWidth - 2] =
                    (byte) ((columns[width - 1] * 3 + columns[width - 2] + 8) >> 4);
                output[destination + outWidth - 1] = (byte) ((columns[width - 1] * 4 + 7) >> 4);
            }

            return output;
        }

        private static int Ceil(int value, int divisor) {
            return (value + divisor - 1) / divisor;
        }

        private static double[,] BuildCosines() {
            var cosines = new double[BlockSize, BlockSize];
            for (int x = 0; x < BlockSize; x++) {
                for (int u = 0; u < BlockSize; u++) {
                    double scale = u == 0 ? Math.Sqrt(0.5) : 1.0;
                    cosines[x, u] = scale * Math.Cos((2 * x + 1) * u * Math.PI / 16.0);
                }
            }
            return cosines;
        }

        /// <summary>
        ///     One Huffman table in the form the specification's <c>DECODE</c> procedure wants.
        /// </summary>
        private sealed class HuffmanCodes {
            /// <summary>Smallest code of each length, indexed 1..16.</summary>
            public readonly int[] MinCode = new int[17];

            /// <summary>Largest code of each length, or -1 when no code has that length.</summary>
            public readonly int[] MaxCode = new int[17];

            /// <summary>Where each length's symbols begin in <see cref="Symbols"/>.</summary>
            public readonly int[] ValuePointer = new int[17];

            /// <summary>The symbols in code order.</summary>
            public readonly byte[] Symbols;

            /// <summary>Builds the code ranges from a table's counts.</summary>
            /// <param name="table">The table as the file states it.</param>
            public HuffmanCodes(JpegHuffmanTable table) {
                Symbols = table.Symbols;

                int code = 0;
                int symbol = 0;
                for (int length = 1; length <= 16; length++) {
                    int count = table.Counts[length - 1];
                    ValuePointer[length] = symbol;
                    MinCode[length] = code;
                    code += count;
                    symbol += count;
                    MaxCode[length] = count == 0 ? -1 : code - 1;
                    code <<= 1;
                }

                if (symbol != table.Symbols.Length)
                    throw new InvalidDataException(
                        $"A Huffman table's counts describe {symbol} codes but it carries {table.Symbols.Length} " +
                        "symbols.");
            }
        }

        /// <summary>
        ///     Reads the entropy-coded segment a bit at a time, undoing the <c>FF 00</c> stuffing.
        /// </summary>
        private sealed class EntropyReader {
            private readonly byte[] _data;
            private int _at;
            private int _bits;
            private int _available;

            /// <summary>Binds a reader to the stuffed scan bytes.</summary>
            /// <param name="data">The entropy-coded data, stuffing intact.</param>
            public EntropyReader(byte[] data) {
                _data = data;
            }

            /// <summary>How many bytes have been consumed, for an exact-consumption check.</summary>
            public int BytesConsumed => _at;

            /// <summary>Reads one bit.</summary>
            /// <returns>The bit.</returns>
            /// <exception cref="EndOfStreamException">The scan ran out of data.</exception>
            /// <exception cref="InvalidDataException">A marker appeared where data was expected.</exception>
            public int ReadBit() {
                if (_available == 0) {
                    if (_at >= _data.Length)
                        throw new EndOfStreamException(
                            "The entropy-coded scan ran out of bits before the last block was decoded.");

                    int value = _data[_at++];
                    if (value == 0xFF) {
                        if (_at >= _data.Length)
                            throw new InvalidDataException("The scan ends on a lone 0xFF.");
                        int next = _data[_at];
                        if (next != 0x00)
                            throw new InvalidDataException(
                                $"Marker FF{next:X2} appeared inside the scan where a stuffed byte was expected.");
                        _at++;
                    }

                    _bits = value;
                    _available = 8;
                }

                _available--;
                return (_bits >> _available) & 1;
            }

            /// <summary>Reads an n-bit big-endian value.</summary>
            /// <param name="count">How many bits.</param>
            /// <returns>The value.</returns>
            public int Receive(int count) {
                int value = 0;
                for (int i = 0; i < count; i++)
                    value = (value << 1) | ReadBit();
                return value;
            }

            /// <summary>Decodes one symbol.</summary>
            /// <param name="table">The table to decode against.</param>
            /// <returns>The symbol.</returns>
            /// <exception cref="InvalidDataException">No code of any length matched.</exception>
            public int DecodeSymbol(HuffmanCodes table) {
                int code = ReadBit();
                for (int length = 1; length <= 16; length++) {
                    if (table.MaxCode[length] >= 0 && code <= table.MaxCode[length])
                        return table.Symbols[table.ValuePointer[length] + code - table.MinCode[length]];
                    code = (code << 1) | ReadBit();
                }

                throw new InvalidDataException("No Huffman code up to sixteen bits matched the scan.");
            }

            /// <summary>
            ///     Discards the padding bits and steps over a restart marker.
            /// </summary>
            /// <remarks>
            ///     No index-32 image declares a restart interval, so nothing in this cache reaches
            ///     it. It is implemented because a decoder that ignored restart markers would
            ///     desynchronise on the first file that carried one and produce a picture rather
            ///     than an error.
            /// </remarks>
            /// <exception cref="InvalidDataException">The expected restart marker is absent.</exception>
            public void SyncToRestart() {
                _available = 0;
                if (_at + 1 >= _data.Length || _data[_at] != 0xFF || !JpegSegment.IsRestart(_data[_at + 1]))
                    throw new InvalidDataException(
                        $"A restart marker was due at {_at} of the scan and is not there.");
                _at += 2;
            }
        }
    }
}
