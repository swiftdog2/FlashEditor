using System;
using System.IO;

namespace FlashEditor.Definitions.Audio.Sfx2.Vorbis {
    /// <summary>
    ///     A Vorbis floor 1: the coarse spectral envelope, stored as line segments between points.
    /// </summary>
    /// <remarks>
    ///     Transcribed from <c>Class56</c> (Class56.java:98-299). Floor 0 does not occur here -
    ///     <c>Class56.java:100-103</c> throws outright on any type but 1, so a floor 0 in this cache
    ///     would crash the client and cannot be present.
    ///     <para>
    ///     The decode and the application are two calls with the curve held between them
    ///     (<c>method510</c> then <c>method513</c>), because the residue is read from the packet in
    ///     between and the bit order is not negotiable. The client keeps that intermediate state in
    ///     static arrays shared by every floor; here it is a <see cref="VorbisFloorScratch"/> the
    ///     caller owns, so two decoders cannot corrupt each other's curve.
    ///     </para>
    /// </remarks>
    internal sealed class VorbisFloor {
        /// <summary>
        ///     The four amplitude ranges a floor's multiplier selects between.
        /// </summary>
        /// <remarks><c>Class56.anIntArray456</c> (:46), indexed by <c>multiplier - 1</c>.</remarks>
        private static readonly int[] Ranges = { 256, 128, 86, 64 };

        /// <summary>
        ///     The inverse decibel table the curve is rendered through.
        /// </summary>
        /// <remarks>
        ///     <c>Class56.aFloatArray445</c> (:7-42), 256 entries, verbatim. It is the whole of the
        ///     floor's amplitude scale: a y value is an index into this, not a gain.
        /// </remarks>
        private static readonly float[] InverseDb = {
            1.0649863E-7F, 1.1341951E-7F, 1.2079015E-7F, 1.2863978E-7F, 1.369995E-7F,
            1.459025E-7F, 1.5538409E-7F, 1.6548181E-7F, 1.7623574E-7F, 1.8768856E-7F, 1.998856E-7F, 2.128753E-7F,
            2.2670913E-7F, 2.4144197E-7F, 2.5713223E-7F, 2.7384212E-7F, 2.9163792E-7F, 3.1059022E-7F, 3.307741E-7F,
            3.5226967E-7F, 3.7516213E-7F, 3.995423E-7F, 4.255068E-7F, 4.5315863E-7F, 4.8260745E-7F, 5.1397E-7F,
            5.4737063E-7F, 5.829419E-7F, 6.208247E-7F, 6.611694E-7F, 7.041359E-7F, 7.4989464E-7F, 7.98627E-7F,
            8.505263E-7F, 9.057983E-7F, 9.646621E-7F, 1.0273513E-6F, 1.0941144E-6F, 1.1652161E-6F, 1.2409384E-6F,
            1.3215816E-6F, 1.4074654E-6F, 1.4989305E-6F, 1.5963394E-6F, 1.7000785E-6F, 1.8105592E-6F, 1.9282195E-6F,
            2.053526E-6F, 2.1869757E-6F, 2.3290977E-6F, 2.4804558E-6F, 2.6416496E-6F, 2.813319E-6F, 2.9961443E-6F,
            3.1908505E-6F, 3.39821E-6F, 3.619045E-6F, 3.8542307E-6F, 4.1047006E-6F, 4.371447E-6F, 4.6555283E-6F,
            4.958071E-6F, 5.280274E-6F, 5.623416E-6F, 5.988857E-6F, 6.3780467E-6F, 6.7925284E-6F, 7.2339453E-6F,
            7.704048E-6F, 8.2047E-6F, 8.737888E-6F, 9.305725E-6F, 9.910464E-6F, 1.0554501E-5F, 1.1240392E-5F,
            1.1970856E-5F, 1.2748789E-5F, 1.3577278E-5F, 1.4459606E-5F, 1.5399271E-5F, 1.6400005E-5F, 1.7465769E-5F,
            1.8600793E-5F, 1.9809577E-5F, 2.1096914E-5F, 2.2467912E-5F, 2.3928002E-5F, 2.5482977E-5F, 2.7139005E-5F,
            2.890265E-5F, 3.078091E-5F, 3.2781227E-5F, 3.4911533E-5F, 3.718028E-5F, 3.9596467E-5F, 4.2169668E-5F,
            4.491009E-5F, 4.7828602E-5F, 5.0936775E-5F, 5.424693E-5F, 5.7772202E-5F, 6.152657E-5F, 6.552491E-5F,
            6.9783084E-5F, 7.4317984E-5F, 7.914758E-5F, 8.429104E-5F, 8.976875E-5F, 9.560242E-5F, 1.0181521E-4F,
            1.0843174E-4F, 1.1547824E-4F, 1.2298267E-4F, 1.3097477E-4F, 1.3948625E-4F, 1.4855085E-4F, 1.5820454E-4F,
            1.6848555E-4F, 1.7943469E-4F, 1.9109536E-4F, 2.0351382E-4F, 2.167393E-4F, 2.3082423E-4F, 2.4582449E-4F,
            2.6179955E-4F, 2.7881275E-4F, 2.9693157E-4F, 3.1622787E-4F, 3.3677815E-4F, 3.5866388E-4F, 3.8197188E-4F,
            4.0679457E-4F, 4.3323037E-4F, 4.613841E-4F, 4.913675E-4F, 5.2329927E-4F, 5.573062E-4F, 5.935231E-4F,
            6.320936E-4F, 6.731706E-4F, 7.16917E-4F, 7.635063E-4F, 8.1312325E-4F, 8.6596457E-4F, 9.2223985E-4F,
            9.821722E-4F, 0.0010459992F, 0.0011139743F, 0.0011863665F, 0.0012634633F, 0.0013455702F, 0.0014330129F,
            0.0015261382F, 0.0016253153F, 0.0017309374F, 0.0018434235F, 0.0019632196F, 0.0020908006F, 0.0022266726F,
            0.0023713743F, 0.0025254795F, 0.0026895993F, 0.0028643848F, 0.0030505287F, 0.003248769F, 0.0034598925F,
            0.0036847359F, 0.0039241905F, 0.0041792067F, 0.004450795F, 0.004740033F, 0.005048067F, 0.0053761187F,
            0.005725489F, 0.0060975635F, 0.0064938175F, 0.0069158226F, 0.0073652514F, 0.007843887F, 0.008353627F,
            0.008896492F, 0.009474637F, 0.010090352F, 0.01074608F, 0.011444421F, 0.012188144F, 0.012980198F,
            0.013823725F, 0.014722068F, 0.015678791F, 0.016697686F, 0.017782796F, 0.018938422F, 0.020169148F,
            0.021479854F, 0.022875736F, 0.02436233F, 0.025945531F, 0.027631618F, 0.029427277F, 0.031339627F,
            0.03337625F, 0.035545226F, 0.037855156F, 0.0403152F, 0.042935107F, 0.045725275F, 0.048696756F, 0.05186135F,
            0.05523159F, 0.05882085F, 0.062643364F, 0.06671428F, 0.07104975F, 0.075666964F, 0.08058423F, 0.08582105F,
            0.09139818F, 0.097337745F, 0.1036633F, 0.11039993F, 0.11757434F, 0.12521498F, 0.13335215F, 0.14201812F,
            0.15124726F, 0.16107617F, 0.1715438F, 0.18269168F, 0.19456401F, 0.20720787F, 0.22067343F, 0.23501402F,
            0.25028655F, 0.26655158F, 0.28387362F, 0.3023213F, 0.32196787F, 0.34289113F, 0.36517414F, 0.3889052F,
            0.41417846F, 0.44109413F, 0.4697589F, 0.50028646F, 0.53279793F, 0.5674221F, 0.6042964F, 0.64356697F,
            0.6853896F, 0.72993004F, 0.777365F, 0.8278826F, 0.88168305F, 0.9389798F, 1.0F
        };

        private readonly int[] partitionClasses;
        private readonly int[] classDimensions;
        private readonly int[] classSubclasses;
        private readonly int[] classMasterbooks;
        private readonly int[][] classSubclassBooks;
        private readonly int multiplier;
        private readonly int[] positions;

        /// <summary>How many points the curve has, which sizes the scratch a caller must supply.</summary>
        internal int PointCount => positions.Length;

        /// <summary>Reads one floor configuration from the setup header.</summary>
        /// <param name="reader">The setup header's bit reader.</param>
        /// <exception cref="InvalidDataException">The floor is not type 1.</exception>
        internal VorbisFloor(Sfx2BitReader reader) {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            int type = reader.Read(16);
            if (type != 1)
                throw new InvalidDataException(
                    "Floor type " + type + " at bit " + (reader.BitPosition - 16) + "; the client throws on " +
                    "anything but type 1 (Class56.java:100-103), so this cache cannot legitimately hold one " +
                    "and the field before it was read at the wrong width.");

            int partitions = reader.Read(5);
            partitionClasses = new int[partitions];
            int classCount = 0;
            for (int i = 0; i < partitions; i++) {
                int partitionClass = reader.Read(4);
                partitionClasses[i] = partitionClass;
                if (partitionClass >= classCount)
                    classCount = partitionClass + 1;
            }

            classDimensions = new int[classCount];
            classSubclasses = new int[classCount];
            classMasterbooks = new int[classCount];
            classSubclassBooks = new int[classCount][];

            for (int i = 0; i < classCount; i++) {
                classDimensions[i] = reader.Read(3) + 1;
                int subclasses = classSubclasses[i] = reader.Read(2);
                if (subclasses != 0)
                    classMasterbooks[i] = reader.Read(8);

                var books = new int[1 << subclasses];
                classSubclassBooks[i] = books;
                for (int j = 0; j < books.Length; j++)
                    books[j] = reader.Read(8) - 1;
            }

            multiplier = reader.Read(2) + 1;
            int rangeBits = reader.Read(4);

            int points = 2;
            for (int i = 0; i < partitions; i++)
                points += classDimensions[partitionClasses[i]];

            positions = new int[points];
            positions[0] = 0;
            positions[1] = 1 << rangeBits;

            int next = 2;
            for (int i = 0; i < partitions; i++) {
                int partitionClass = partitionClasses[i];
                for (int j = 0; j < classDimensions[partitionClass]; j++)
                    positions[next++] = reader.Read(rangeBits);
            }
        }

        /// <summary>
        ///     Reads this packet's curve, or reports that the packet carries no floor at all.
        /// </summary>
        /// <remarks>
        ///     <c>Class56.method510</c> (:154-185). A cleared first bit means the whole block is
        ///     silent, and the client then skips the residue too - so the return value is not a
        ///     success flag and treating it as one would decode a silent block as noise.
        /// </remarks>
        /// <param name="reader">The packet's bit reader.</param>
        /// <param name="codebooks">The setup header's codebooks.</param>
        /// <param name="scratch">Where to leave the curve.</param>
        /// <returns>Whether the packet carries a floor.</returns>
        internal bool DecodeCurve(Sfx2BitReader reader, VorbisCodebook[] codebooks, VorbisFloorScratch scratch) {
            if (reader.ReadBit() == 0)
                return false;

            int points = positions.Length;
            Array.Copy(positions, scratch.Positions, points);

            int range = Ranges[multiplier - 1];
            int amplitudeBits = VorbisMath.Ilog(range - 1);
            scratch.Levels[0] = reader.Read(amplitudeBits);
            scratch.Levels[1] = reader.Read(amplitudeBits);

            int next = 2;
            for (int partition = 0; partition < partitionClasses.Length; partition++) {
                int partitionClass = partitionClasses[partition];
                int dimensions = classDimensions[partitionClass];
                int subclassBits = classSubclasses[partitionClass];
                int subclassMask = (1 << subclassBits) - 1;

                int classword = 0;
                if (subclassBits > 0)
                    classword = codebooks[classMasterbooks[partitionClass]].DecodeScalar(reader);

                for (int j = 0; j < dimensions; j++) {
                    int book = classSubclassBooks[partitionClass][classword & subclassMask];
                    classword = (int) ((uint) classword >> subclassBits);
                    scratch.Levels[next++] = book >= 0 ? codebooks[book].DecodeScalar(reader) : 0;
                }
            }

            return true;
        }

        /// <summary>
        ///     Multiplies the decoded curve into the residue spectrum.
        /// </summary>
        /// <remarks>
        ///     <c>Class56.method513</c> (:213-258). Three stages: reconstruct each point's absolute
        ///     level from its two neighbours and its stored difference, sort the points by position,
        ///     then draw a line between consecutive points that were actually stored and scale the
        ///     spectrum by the inverse-decibel value along it.
        ///     <para>
        ///     The scratch is destroyed here - the sort reorders it in place - so a caller must
        ///     decode a curve again before applying one again.
        ///     </para>
        /// </remarks>
        /// <param name="scratch">The curve, as left by <see cref="DecodeCurve"/>.</param>
        /// <param name="spectrum">The residue spectrum to scale.</param>
        /// <param name="length">How many bins of it are in use.</param>
        internal void ApplyCurve(VorbisFloorScratch scratch, float[] spectrum, int length) {
            int points = positions.Length;
            int range = Ranges[multiplier - 1];
            int[] xs = scratch.Positions;
            int[] ys = scratch.Levels;
            bool[] stored = scratch.Stored;

            stored[0] = stored[1] = true;

            for (int i = 2; i < points; i++) {
                int lower = LowNeighbour(xs, i);
                int higher = HighNeighbour(xs, i);
                int predicted = RenderPoint(xs[lower], ys[lower], xs[higher], ys[higher], xs[i]);

                int difference = ys[i];
                int headroom = range - predicted;
                int footroom = predicted;
                int span = (headroom < footroom ? headroom : footroom) << 1;

                if (difference != 0) {
                    stored[lower] = stored[higher] = true;
                    stored[i] = true;
                    if (difference >= span) {
                        ys[i] = headroom > footroom
                            ? difference - footroom + predicted
                            : predicted - difference + headroom - 1;
                    } else {
                        ys[i] = (difference & 1) != 0
                            ? predicted - (difference + 1) / 2
                            : predicted + difference / 2;
                    }
                } else {
                    stored[i] = false;
                    ys[i] = predicted;
                }
            }

            Sort(xs, ys, stored, 0, points - 1);

            int lastX = 0;
            int lastY = ys[0] * multiplier;
            for (int i = 1; i < points; i++) {
                if (!stored[i])
                    continue;

                int x = xs[i];
                int y = ys[i] * multiplier;
                RenderLine(lastX, lastY, x, y, spectrum, length);
                if (x >= length)
                    return;

                lastX = x;
                lastY = y;
            }

            float tail = InverseDb[lastY];
            for (int i = lastX; i < length; i++)
                spectrum[i] *= tail;
        }

        /// <summary>The index of the point with the greatest position below this one's.</summary>
        /// <remarks><c>Class56.method512</c> (:48-61).</remarks>
        /// <param name="xs">The positions.</param>
        /// <param name="index">The point being placed.</param>
        /// <returns>The neighbour's index.</returns>
        private static int LowNeighbour(int[] xs, int index) {
            int target = xs[index];
            int best = -1;
            int bestValue = int.MinValue;
            for (int i = 0; i < index; i++) {
                int value = xs[i];
                if (value < target && value > bestValue) {
                    best = i;
                    bestValue = value;
                }
            }

            return best;
        }

        /// <summary>The index of the point with the least position above this one's.</summary>
        /// <remarks><c>Class56.method516</c> (:72-85).</remarks>
        /// <param name="xs">The positions.</param>
        /// <param name="index">The point being placed.</param>
        /// <returns>The neighbour's index.</returns>
        private static int HighNeighbour(int[] xs, int index) {
            int target = xs[index];
            int best = -1;
            int bestValue = int.MaxValue;
            for (int i = 0; i < index; i++) {
                int value = xs[i];
                if (value > target && value < bestValue) {
                    best = i;
                    bestValue = value;
                }
            }

            return best;
        }

        /// <summary>Where the line between two points passes through a third position.</summary>
        /// <remarks><c>Class56.method517</c> (:288-299). Integer division, truncating toward zero.</remarks>
        /// <param name="x0">First point's position.</param>
        /// <param name="y0">First point's level.</param>
        /// <param name="x1">Second point's position.</param>
        /// <param name="y1">Second point's level.</param>
        /// <param name="x">The position being interpolated.</param>
        /// <returns>The interpolated level.</returns>
        private static int RenderPoint(int x0, int y0, int x1, int y1, int x) {
            int dy = y1 - y0;
            int adx = x1 - x0;
            int ady = dy < 0 ? -dy : dy;
            int offset = ady * (x - x0) / adx;
            return dy < 0 ? y0 - offset : y0 + offset;
        }

        /// <summary>
        ///     Scales one run of the spectrum by the inverse-decibel values along a line.
        /// </summary>
        /// <remarks>
        ///     <c>Class56.method511</c> (:187-210), a Bresenham walk. The first bin is always scaled
        ///     even when the run is clipped by <paramref name="length"/>, which is why the clip is
        ///     applied after it rather than to the loop bound.
        /// </remarks>
        /// <param name="x0">Where the line starts.</param>
        /// <param name="y0">The level there.</param>
        /// <param name="x1">Where it ends.</param>
        /// <param name="y1">The level there.</param>
        /// <param name="spectrum">The spectrum to scale.</param>
        /// <param name="length">How many bins are in use.</param>
        private static void RenderLine(int x0, int y0, int x1, int y1, float[] spectrum, int length) {
            int dy = y1 - y0;
            int adx = x1 - x0;
            int ady = dy < 0 ? -dy : dy;
            int step = dy / adx;
            int y = y0;
            int error = 0;
            int stepPlus = dy < 0 ? step - 1 : step + 1;
            ady -= (step < 0 ? -step : step) * adx;

            spectrum[x0] *= InverseDb[y];

            if (x1 > length)
                x1 = length;

            for (int x = x0 + 1; x < x1; x++) {
                error += ady;
                if (error >= adx) {
                    error -= adx;
                    y += stepPlus;
                } else {
                    y += step;
                }

                spectrum[x] *= InverseDb[y];
            }
        }

        /// <summary>
        ///     Sorts the three parallel curve arrays by position.
        /// </summary>
        /// <remarks>
        ///     <c>Class56.method514</c> (:261-286), transcribed rather than replaced with a library
        ///     sort. Its partition step is not the textbook one and it is not stable, so two points
        ///     at the same position come out in an order that is a property of this exact code -
        ///     and floor positions are not guaranteed distinct.
        /// </remarks>
        /// <param name="xs">Positions.</param>
        /// <param name="ys">Levels.</param>
        /// <param name="stored">Whether each point was stored.</param>
        /// <param name="low">First index of the range to sort.</param>
        /// <param name="high">Last index of the range to sort.</param>
        private static void Sort(int[] xs, int[] ys, bool[] stored, int low, int high) {
            if (low >= high)
                return;

            int pivot = low;
            int pivotX = xs[pivot];
            int pivotY = ys[pivot];
            bool pivotStored = stored[pivot];

            for (int i = low + 1; i <= high; i++) {
                int x = xs[i];
                if (x >= pivotX)
                    continue;

                xs[pivot] = x;
                ys[pivot] = ys[i];
                stored[pivot] = stored[i];
                pivot++;
                xs[i] = xs[pivot];
                ys[i] = ys[pivot];
                stored[i] = stored[pivot];
            }

            xs[pivot] = pivotX;
            ys[pivot] = pivotY;
            stored[pivot] = pivotStored;

            Sort(xs, ys, stored, low, pivot - 1);
            Sort(xs, ys, stored, pivot + 1, high);
        }
    }

    /// <summary>
    ///     The per-packet floor curve, held between the read and the application of it.
    /// </summary>
    /// <remarks>
    ///     The client keeps this in three static arrays on <c>Class56</c> (:43-45) sized to the
    ///     largest floor it has seen, which works because the client decodes one stream at a time.
    ///     Making it an object the caller owns is the one deliberate divergence in this decoder, and
    ///     it changes nothing about what is read.
    /// </remarks>
    internal sealed class VorbisFloorScratch {
        /// <summary>Each point's position along the spectrum.</summary>
        internal int[] Positions { get; }

        /// <summary>Each point's level, as an index into the inverse-decibel table once scaled.</summary>
        internal int[] Levels { get; }

        /// <summary>Whether each point was stored in the packet, as opposed to interpolated.</summary>
        internal bool[] Stored { get; }

        /// <summary>Allocates scratch big enough for any floor in a setup header.</summary>
        /// <param name="points">The largest point count of any floor.</param>
        internal VorbisFloorScratch(int points) {
            Positions = new int[points];
            Levels = new int[points];
            Stored = new bool[points];
        }
    }
}
