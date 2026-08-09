using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Compression {
    /// <summary>
    ///     The chat-text Huffman table: index 10's single file, and the codewords and decode tree
    ///     the client derives from it.
    /// </summary>
    /// <remarks>
    ///     JS5 index 10 (<c>RSConstants.HUFFMAN_INDEX</c>), one group holding one file.
    ///     <c>InterfaceSettings.java:310</c> builds it as
    ///     <c>new Class213(aJS5Archive_4198.method2739("huffman", "", -32734))</c> - resolved by
    ///     <b>name</b>, never by id - and it is the codec for chat text and nothing else:
    ///     <c>Class284_Sub1_Sub1.method3368</c> writes a smart length then the compressed string into
    ///     an outbound packet, and <c>Node_Sub10_Sub26.method1084</c> reads the pair back.
    ///     <para>
    ///     <b>The file is bit lengths and nothing else.</b> <c>Class213.java:195</c> reads
    ///     <c>int i_27_ = is[i_26_]</c> straight out of the byte array and uses it as a shift
    ///     distance, so record <c>i</c> is the codeword length for data value <c>i</c>. A length of
    ///     0 is skipped at <c>:197</c> and means that byte value has no codeword at all; the
    ///     compressor throws for it at <c>:296-298</c>.
    ///     </para>
    ///     <para>
    ///     <b>The stored bytes and the derived structures are kept apart on purpose.</b>
    ///     <see cref="Encode"/> writes back the array <see cref="Decode"/> read and never
    ///     reconstructs it from the codewords or the tree, because the derivation loses the
    ///     distinction it would have to recover: a value with no codeword and the first value with a
    ///     codeword both hold 0 in the codeword array, and only the stored byte tells them apart.
    ///     Only <see cref="SetBitLength"/> may change a stored byte, and it rebuilds the derived
    ///     state from the array afterwards rather than patching it.
    ///     </para>
    ///     <para>
    ///     <b>The codeword assignment is not textbook canonical Huffman.</b>
    ///     <c>Class213.java:191-235</c> walks the data values in order rather than sorted by length,
    ///     keeping a per-length working array and backtracking into the shorter lengths when a
    ///     prefix it needs has already been consumed. Measured against the shipped table, a
    ///     canonical assignment over the same lengths agrees on exactly one of the 256 values. A
    ///     substitution would still round-trip against itself and would disagree with the client on
    ///     the wire, which is why <see cref="Rebuild"/> is a literal port and why the codec tests pin
    ///     codewords rather than only round trips.
    ///     </para>
    /// </remarks>
    public sealed class HuffmanTable {
        /// <summary>
        ///     Records the file holds, one per possible byte value.
        /// </summary>
        /// <remarks>
        ///     A format constant, not a measurement. <c>Class213.java:294</c> indexes the length
        ///     array with <c>0xff &amp; is_6_[i_4_]</c>, an unsigned byte, so a table shorter than
        ///     this cannot serve every input the compressor is handed and the client would throw on
        ///     the first byte past its end.
        /// </remarks>
        public const int EntryCount = 256;

        /// <summary>
        ///     Longest codeword the construction can carry.
        /// </summary>
        /// <remarks>
        ///     <c>Class213.java:191</c> sizes its working array at 33 and indexes it by the bit
        ///     length, so 32 is the last length that addresses a slot. A stored byte outside 0..32
        ///     puts the client out of bounds rather than producing a wrong tree, so this decoder
        ///     refuses it instead of carrying on.
        /// </remarks>
        public const int MaxBitLength = 32;

        /// <summary>The name the client resolves index 10's group by.</summary>
        /// <remarks>
        ///     Its hash is the group identifier the reference table carries, which is what makes the
        ///     join provable rather than plausible. The group id is <b>1</b> in both supported
        ///     caches, but the client never uses it.
        /// </remarks>
        public const string GroupName = "huffman";

        /// <summary>
        ///     The file exactly as stored: one bit length per data value.
        /// </summary>
        /// <remarks>
        ///     The single source of truth for <see cref="Encode"/>. Held as raw bytes rather than as
        ///     the signed lengths the client reads, so a byte the client would reject still
        ///     re-encodes to itself.
        /// </remarks>
        private readonly byte[] _storedLengths;

        /// <summary>Derived: the left-aligned 32-bit codeword for each data value.</summary>
        private int[] _codewords = Array.Empty<int>();

        /// <summary>Derived: the decode tree, negative entries being leaves.</summary>
        private int[] _tree = Array.Empty<int>();

        /// <summary>Builds a table over a copy of the stored bit lengths.</summary>
        /// <param name="storedLengths">One bit length per data value.</param>
        /// <exception cref="InvalidDataException">A length is outside the range the client can use.</exception>
        public HuffmanTable(byte[] storedLengths) {
            if (storedLengths == null)
                throw new ArgumentNullException(nameof(storedLengths));

            _storedLengths = (byte[]) storedLengths.Clone();
            Rebuild();
        }

        /// <summary>
        ///     Reads the whole table out of a group's single file.
        /// </summary>
        /// <remarks>
        ///     Takes exactly <see cref="EntryCount"/> bytes rather than everything remaining. The
        ///     record carries no length of its own, so "consume to the end of the buffer" would make
        ///     any file length look correct - including one the client could not index.
        /// </remarks>
        /// <param name="stream">The stored file, positioned at its start.</param>
        /// <returns>The decoded table.</returns>
        /// <exception cref="InvalidDataException">The file is short, or holds an unusable length.</exception>
        public static HuffmanTable Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (stream.Remaining() < EntryCount)
                throw new InvalidDataException(
                    "The Huffman table is " + stream.Remaining() + " bytes but the client indexes it " +
                    "with an unsigned byte, so it must hold " + EntryCount + ".");

            return new HuffmanTable(stream.ReadBytes(EntryCount));
        }

        /// <summary>
        ///     Finds the chat table in an open cache the way the client finds it.
        /// </summary>
        /// <remarks>
        ///     By name hash rather than by id. <c>InterfaceSettings.java:310</c> asks index 10 for
        ///     <c>"huffman"</c> and never mentions a group id; the id happens to be 1 in both
        ///     supported caches, and hardcoding it would break on any cache that renumbered the
        ///     group while leaving the name intact.
        ///     <para>
        ///     The fallback to a lone declared group covers a table with the identifiers flag clear,
        ///     where no name is recorded to hash against. It is not reached in either supported
        ///     cache - both set the flag and carry <c>hash("huffman")</c>.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The group and file the table is stored as.</returns>
        /// <exception cref="FileNotFoundException">The index holds no table this could be.</exception>
        public static (int GroupId, int FileId) Locate(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            //Throws for an index with no table, which is the right answer for a cache that cannot
            //hold a chat table at all.
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.HUFFMAN_INDEX);

            int groupId = table.GetArchiveId(GroupName);
            if (groupId < 0) {
                var declared = new List<int>(table.GetArchiveEntries().Keys);
                if (declared.Count != 1)
                    throw new FileNotFoundException(
                        "Index " + RSConstants.HUFFMAN_INDEX + " has no group named '" + GroupName +
                        "' and declares " + declared.Count + " groups, so which one is the chat " +
                        "table cannot be established.");
                groupId = declared[0];
            }

            int[] fileIds = table.GetArchiveEntry(groupId)?.GetValidFileIds() ?? Array.Empty<int>();
            if (fileIds.Length != 1)
                throw new FileNotFoundException(
                    "Index " + RSConstants.HUFFMAN_INDEX + " group " + groupId + " declares " +
                    fileIds.Length + " files; the chat table is a single-file group.");

            return (groupId, fileIds[0]);
        }

        /// <summary>Reads and decodes the chat table out of an open cache.</summary>
        /// <param name="cache">The open cache.</param>
        /// <returns>The decoded table.</returns>
        public static HuffmanTable Load(RSCache cache) {
            (int groupId, int fileId) = Locate(cache);
            return Decode(new JagStream(cache.ReadFileBytes(RSConstants.HUFFMAN_INDEX, groupId, fileId)));
        }

        /// <summary>
        ///     Writes the stored bit lengths back verbatim.
        /// </summary>
        /// <remarks>
        ///     Verbatim rather than recomputed. Nothing in the record has a second legal
        ///     representation, so byte identity here is only ever at risk from an encoder that tries
        ///     to derive the lengths back out of the codewords.
        /// </remarks>
        /// <returns>The bytes to store, ready to read.</returns>
        public JagStream Encode() {
            var stream = new JagStream(_storedLengths.Length);
            stream.Write(_storedLengths, 0, _storedLengths.Length);
            return stream.Flip();
        }

        /// <summary>Data values the table has a record for.</summary>
        public int Entries => _storedLengths.Length;

        /// <summary>
        ///     Nodes in the derived decode tree.
        /// </summary>
        /// <remarks>Exposed so a test can pin the shape the tree walk depends on.</remarks>
        public int TreeSize => _tree.Length;

        /// <summary>
        ///     The codeword length for a data value, or 0 when it has no codeword.
        /// </summary>
        /// <remarks>
        ///     Signed, because <c>Class213.java:195</c> reads a Java <c>byte</c>. A stored 0x80-0xFF
        ///     is therefore a negative length, which is rejected at construction rather than
        ///     silently treated as a long one.
        /// </remarks>
        /// <param name="value">The data value, 0 to <see cref="Entries"/> - 1.</param>
        /// <returns>The bit length.</returns>
        public int BitLengthOf(int value) => (sbyte) _storedLengths[value];

        /// <summary>
        ///     The codeword for a data value, left-aligned in 32 bits.
        /// </summary>
        /// <remarks>
        ///     Left-aligned because the compressor shifts it down rather than up
        ///     (<c>Class213.java:308</c>), so the significant bits are the top
        ///     <see cref="BitLengthOf"/> of them. 0 for a value with no codeword.
        /// </remarks>
        /// <param name="value">The data value.</param>
        /// <returns>The codeword.</returns>
        public int CodewordOf(int value) => _codewords[value];

        /// <summary>
        ///     A data value's codeword written out as bits, or an empty string when it has none.
        /// </summary>
        /// <param name="value">The data value.</param>
        /// <returns>The codeword in binary, most significant bit first.</returns>
        public string CodewordBits(int value) {
            int length = BitLengthOf(value);
            if (length <= 0)
                return string.Empty;

            char[] bits = new char[length];
            for (int i = 0; i < length; i++)
                bits[i] = (_codewords[value] & (int) (0x80000000u >> i)) != 0 ? '1' : '0';
            return new string(bits);
        }

        /// <summary>
        ///     The derived decode tree, for tests and diagnostics.
        /// </summary>
        /// <remarks>
        ///     A negative entry is a leaf holding <c>~value</c>; a non-negative one is the node a
        ///     1-bit moves to, a 0-bit always moving to the next node along. Derived state, so
        ///     nothing may write it back to the cache.
        /// </remarks>
        /// <returns>A copy of the tree.</returns>
        public IReadOnlyList<int> DecodeTree() => (int[]) _tree.Clone();

        /// <summary>
        ///     Changes one data value's stored bit length and rebuilds everything derived from it.
        /// </summary>
        /// <remarks>
        ///     Rebuilds rather than patches: a codeword assignment depends on every entry before it,
        ///     so one changed length moves an unpredictable number of the others.
        /// </remarks>
        /// <param name="value">The data value.</param>
        /// <param name="bitLength">The new length, 0 for "no codeword".</param>
        /// <exception cref="InvalidDataException">The new set of lengths is not usable.</exception>
        public void SetBitLength(int value, int bitLength) {
            if (value < 0 || value >= _storedLengths.Length)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The table holds " + _storedLengths.Length + " entries.");
            if (bitLength < 0 || bitLength > MaxBitLength)
                throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength,
                    "A codeword is 1 to " + MaxBitLength + " bits, or 0 for no codeword.");

            byte previous = _storedLengths[value];
            _storedLengths[value] = (byte) bitLength;
            try {
                Rebuild();
            }
            catch (Exception) {
                //A safety net rather than a live path: the guards above already cover everything
                //Rebuild refuses today. It is here so that adding a check to Rebuild - completeness,
                //say - cannot leave the editor holding a stored array its derived state disagrees
                //with, which is the one state this type exists to make impossible.
                _storedLengths[value] = previous;
                Rebuild();
                throw;
            }
        }

        // ===================================================================
        //  Construction - Class213.java:182-276
        // ===================================================================

        /// <summary>
        ///     Derives the codewords and the decode tree from the stored bit lengths.
        /// </summary>
        /// <remarks>
        ///     A literal port of <c>Class213</c>'s constructor, including the backtrack at
        ///     <c>:206-222</c> that reclaims a prefix from the shorter lengths. That branch is what
        ///     makes the assignment differ from canonical Huffman, and a canonical substitute
        ///     produces a table that decodes its own output and disagrees with the client.
        ///     <para>
        ///     The tree uses 0 as both "no 1-child yet" and a node index, which is safe only because
        ///     the first value with a codeword always gets an all-zero one - its length's working
        ///     entry starts at 0 - so a leaf is allocated before any 1-branch is ever taken.
        ///     </para>
        /// </remarks>
        /// <exception cref="InvalidDataException">A stored length is outside 0..32.</exception>
        private void Rebuild() {
            int entries = _storedLengths.Length;
            var codewords = new int[entries];
            var tree = new int[8];

            //One in-progress codeword per bit length. Index 0 is never a length and is read as the
            //"give the prefix back" source at :226, so the array is 33 long as the client's is.
            var working = new int[MaxBitLength + 1];
            int nextFreeNode = 0;

            for (int value = 0; value < entries; value++) {
                int length = (sbyte) _storedLengths[value];
                if (length == 0)
                    continue;

                if (length < 0 || length > MaxBitLength)
                    throw new InvalidDataException(
                        "Data value " + value + " has a stored bit length of " + length +
                        ", which the client reads as a shift distance and would index its 33-entry " +
                        "working array with. Valid lengths are 0 (no codeword) to " + MaxBitLength + ".");

                int increment = (int) (1u << (32 - length));
                int codeword = working[length];
                codewords[value] = codeword;

                int next;
                if ((increment & codeword) == 0) {
                    //This length has not used its own bit yet, so taking it strands the prefix for
                    //every shorter length sharing this codeword. :206-222 hands each of them the
                    //sibling branch, or the next length up's prefix when the sibling is gone too.
                    for (int shorter = length - 1; shorter >= 1; shorter--) {
                        int held = working[shorter];
                        if (held != codeword)
                            break;

                        int bit = (int) (1u << (32 - shorter));
                        if ((bit & held) == 0) {
                            working[shorter] = bit | held;
                        }
                        else {
                            working[shorter] = working[shorter - 1];
                            break;
                        }
                    }

                    next = increment | codeword;
                }
                else {
                    next = working[length - 1];
                }

                working[length] = next;

                for (int longer = length + 1; longer <= MaxBitLength; longer++)
                    if (working[longer] == codeword)
                        working[longer] = next;

                //Walk the codeword into the tree, allocating a 1-child where there is none.
                int node = 0;
                for (int bit = 0; bit < length; bit++) {
                    if ((codeword & (int) (0x80000000u >> bit)) != 0) {
                        if (tree[node] == 0)
                            tree[node] = nextFreeNode;
                        node = tree[node];
                    }
                    else {
                        node++;
                    }

                    if (node >= tree.Length)
                        Array.Resize(ref tree, tree.Length * 2);
                }

                if (node >= nextFreeNode)
                    nextFreeNode = node + 1;

                tree[node] = ~value;
            }

            _codewords = codewords;
            _tree = tree;
        }

        // ===================================================================
        //  Compression - Class213.method2780, :278-342
        // ===================================================================

        /// <summary>
        ///     Compresses bytes into a destination buffer, bit-packed.
        /// </summary>
        /// <remarks>
        ///     Argument order follows the caller rather than the obfuscated signature:
        ///     <c>Class284_Sub1_Sub1.method3368</c> passes (source length, destination, source
        ///     offset, destination offset, source), and the return is the byte count written.
        ///     <para>
        ///     Output is written most significant bit first and the final byte is padded with
        ///     whatever the last codeword left, which is why the decompressor is driven by an output
        ///     count rather than by running out of input.
        ///     </para>
        /// </remarks>
        /// <param name="source">The bytes to compress.</param>
        /// <param name="sourceOffset">Where to start reading.</param>
        /// <param name="length">How many bytes to compress.</param>
        /// <param name="destination">Where the packed bits go.</param>
        /// <param name="destinationOffset">Where to start writing.</param>
        /// <returns>Bytes written to <paramref name="destination"/>.</returns>
        /// <exception cref="InvalidOperationException">A source byte has no codeword.</exception>
        public int Compress(byte[] source, int sourceOffset, int length, byte[] destination,
            int destinationOffset) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (sourceOffset < 0 || length < 0 || sourceOffset + length > source.Length)
                throw new ArgumentOutOfRangeException(nameof(length), length,
                    "The requested span runs past the end of the source.");
            if (destinationOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(destinationOffset), destinationOffset,
                    "Offsets are non-negative.");

            int accumulator = 0;
            int bitPosition = destinationOffset << 3;
            int end = sourceOffset + length;

            for (int at = sourceOffset; at < end; at++) {
                int value = source[at] & 0xFF;
                if (value >= _codewords.Length)
                    throw new InvalidOperationException("No codeword for data value " + value +
                        ": the table only holds " + _codewords.Length + " entries.");

                int codeword = _codewords[value];
                int bits = BitLengthOf(value);

                //:296-298 - the client throws here rather than emitting nothing, because a value
                //with no codeword cannot be represented and skipping it would shift the message.
                if (bits == 0)
                    throw new InvalidOperationException("No codeword for data value " + value);

                int first = bitPosition >> 3;
                int offset = bitPosition & 7;

                //:303 - a fresh destination byte starts from nothing, a continued one keeps the
                //bits the previous codeword left in it.
                if (offset == 0)
                    accumulator = 0;

                int last = first + ((offset + bits - 1) >> 3);
                int shift = offset + 24;

                accumulator |= (int) ((uint) codeword >> shift);
                destination[first] = (byte) accumulator;

                //Up to four more bytes, each taking the next slice down. The fifth is unreachable
                //with any length below 26 and is carried because the client carries it.
                for (int spill = first + 1; spill <= last; spill++) {
                    shift -= 8;
                    accumulator = shift >= 0
                        ? (int) ((uint) codeword >> shift)
                        : codeword << -shift;
                    destination[spill] = (byte) accumulator;
                }

                bitPosition += bits;
            }

            return ((bitPosition + 7) >> 3) - destinationOffset;
        }

        // ===================================================================
        //  Decompression - Class213.method2782, :344-502
        // ===================================================================

        /// <summary>
        ///     Expands packed bits back into bytes, stopping once the expected count is out.
        /// </summary>
        /// <remarks>
        ///     The client unrolls the eight bits of a source byte into eight copies of the same
        ///     block (<c>:362-494</c>); this is that block in a loop. Nothing else differs, including
        ///     the return: the byte the last output landed in counts as consumed even when the
        ///     decode stopped part way through it.
        ///     <para>
        ///     The one deliberate divergence is the bounds check. The client walks the source with
        ///     no limit at all and relies on <c>Node_Sub10_Sub26.method1084</c> catching the
        ///     resulting exception and returning the literal "Cabbage"; here a packet that runs out
        ///     of bits before it runs out of characters is reported.
        ///     </para>
        /// </remarks>
        /// <param name="source">The packed bits.</param>
        /// <param name="sourceOffset">Where to start reading.</param>
        /// <param name="destination">Where the expanded bytes go.</param>
        /// <param name="destinationOffset">Where to start writing.</param>
        /// <param name="length">How many bytes to expand.</param>
        /// <returns>Bytes consumed from <paramref name="source"/>.</returns>
        /// <exception cref="InvalidDataException">The packed bits ran out before the count was met.</exception>
        public int Decompress(byte[] source, int sourceOffset, byte[] destination,
            int destinationOffset, int length) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), length, "Lengths are non-negative.");
            if (destinationOffset < 0 || destinationOffset + length > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(length), length,
                    "The expanded bytes run past the end of the destination.");

            if (length == 0)
                return 0;

            int node = 0;
            int written = destinationOffset;
            int end = destinationOffset + length;
            int at = sourceOffset;

            while (true) {
                if (at < 0 || at >= source.Length)
                    throw new InvalidDataException(
                        "The packed bits ended after " + (written - destinationOffset) + " of " +
                        length + " bytes, so the length and the payload disagree.");

                int packed = source[at];

                for (int bit = 0; bit < 8; bit++) {
                    node = (packed & (0x80 >> bit)) != 0 ? _tree[node] : node + 1;

                    int reached = _tree[node];
                    if (reached >= 0)
                        continue;

                    destination[written++] = (byte) ~reached;
                    if (written >= end)
                        return at + 1 - sourceOffset;

                    node = 0;
                }

                at++;
            }
        }

        // ===================================================================
        //  Chat text, the only thing the table is used for
        // ===================================================================

        /// <summary>
        ///     Compresses a chat string.
        /// </summary>
        /// <remarks>
        ///     The text is converted through the client's modified cp1252 first, as
        ///     <c>aa.method152</c> does, so a character outside it becomes '?' rather than two bytes.
        ///     The conversion is routed through <see cref="JagStream.WriteJagexString"/> so the remap
        ///     table stays in one place; the one divergence is an embedded NUL, which that writer
        ///     drops and the client encodes as '?'.
        /// </remarks>
        /// <param name="text">The message.</param>
        /// <returns>The packed bits, and the character count needed to expand them.</returns>
        public (byte[] Packed, int Characters) Compress(string text) {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            byte[] plain = ToChatBytes(text);

            //Sized from the lengths rather than guessed: a codeword may be up to 32 bits, and a
            //buffer one byte short would be an index-out-of-range inside the bit packer.
            long bits = 0;
            foreach (byte value in plain)
                bits += Math.Max(0, BitLengthOf(value));

            var packed = new byte[(int) ((bits + 7) >> 3) + 1];
            int written = Compress(plain, 0, plain.Length, packed, 0);
            Array.Resize(ref packed, written);
            return (packed, plain.Length);
        }

        /// <summary>
        ///     Expands a chat string.
        /// </summary>
        /// <param name="packed">The packed bits.</param>
        /// <param name="characters">How many characters the message holds.</param>
        /// <returns>The message.</returns>
        public string Decompress(byte[] packed, int characters) {
            if (packed == null)
                throw new ArgumentNullException(nameof(packed));

            var plain = new byte[characters];
            Decompress(packed, 0, plain, 0, characters);
            return FromChatBytes(plain);
        }

        /// <summary>
        ///     Frames a chat message the way the client puts it on the wire.
        /// </summary>
        /// <remarks>
        ///     <c>Class284_Sub1_Sub1.method3368</c>: an unsigned smart holding the <b>character</b>
        ///     count, then the packed bits. The count is of characters rather than of packed bytes,
        ///     which is what lets the reader stop mid-byte.
        /// </remarks>
        /// <param name="text">The message.</param>
        /// <returns>The framed packet body.</returns>
        public byte[] EncodeChatMessage(string text) {
            (byte[] packed, int characters) = Compress(text);

            var stream = new JagStream(packed.Length + 2);
            stream.WriteUnsignedSmart(characters);
            stream.Write(packed, 0, packed.Length);
            return stream.Flip().ToArray();
        }

        /// <summary>
        ///     Reads a framed chat message back.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub10_Sub26.method1084</c>. The client clamps the count to a caller-supplied
        ///     maximum before allocating; there is no packet size to clamp against here, so the
        ///     count is taken as written and a payload too short for it is reported by
        ///     <see cref="Decompress(byte[], int, byte[], int, int)"/>.
        /// </remarks>
        /// <param name="packet">The framed packet body.</param>
        /// <returns>The message.</returns>
        public string DecodeChatMessage(byte[] packet) {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            var stream = new JagStream(packet);
            int characters = stream.ReadUnsignedSmart();
            var plain = new byte[characters];

            if (characters > 0)
                Decompress(packet, stream.Position, plain, 0, characters);

            return FromChatBytes(plain);
        }

        /// <summary>The client's modified cp1252, without the terminator the string writer adds.</summary>
        /// <param name="text">The message.</param>
        /// <returns>One byte per character.</returns>
        private static byte[] ToChatBytes(string text) {
            var stream = new JagStream(text.Length + 1);
            stream.WriteJagexString(text);
            byte[] written = stream.Flip().ToArray();
            var plain = new byte[written.Length - 1];
            Array.Copy(written, plain, plain.Length);
            return plain;
        }

        /// <summary>The inverse of <see cref="ToChatBytes"/>.</summary>
        /// <param name="plain">One byte per character.</param>
        /// <returns>The message.</returns>
        private static string FromChatBytes(byte[] plain) {
            var terminated = new byte[plain.Length + 1];
            Array.Copy(plain, terminated, plain.Length);
            return new JagStream(terminated).ReadJagexString();
        }
    }
}
