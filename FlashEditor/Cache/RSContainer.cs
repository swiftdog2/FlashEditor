using static FlashEditor.Utils.DebugUtil;
using System.IO;
using System;
using FlashEditor.Utils;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.IO;

namespace FlashEditor.Cache {
    public class RSContainer {
        private JagStream stream; //the archive stream
        public int indexId;
        public int id;
        public int length;
        public byte compressionType = 0;
        public int version = -1;
        public int decompressedLength = -1;

        //The archive that is represented by the container
        public RSArchive archive;

        /// <summary>Whether this container has been modified and must not be evicted.</summary>
        public bool Dirty { get; set; }

        /// <summary>
        ///     Whether the stored bytes this container was decoded from were XTEA encrypted.
        /// </summary>
        /// <remarks>
        ///     A revision 639 reference table is format 6 and carries no per-archive encryption
        ///     flag, so nothing on disk records which archives are encrypted. The only moment the
        ///     question is answered at all is at decode time, when a key either fits or does not,
        ///     which is why the answer is recorded here rather than re-derived on the way out.
        ///     Re-deriving it from "a key is held for this archive" is the ambiguity that loses
        ///     map squares: a key dump covers a whole build, so it names far more archives than
        ///     are actually encrypted in any given cache.
        /// </remarks>
        public bool StoredEncrypted { get; private set; }

        /// <summary>
        ///     Whether the bytes the file store holds for this container still encode the payload
        ///     it is currently carrying.
        /// </summary>
        /// <remarks>
        ///     Set when <see cref="Decode"/> reads a container out of the store, and set again by
        ///     the write path once it has stored the payload it holds. It is what lets a save that
        ///     changes nothing write nothing: deflate is not canonical, so re-encoding an
        ///     unmodified payload produces different - equally valid - stored bytes, and the
        ///     archive CRC is taken over the STORED bytes. Rewriting them therefore changes the
        ///     CRC, the reference table entry, and the entry of every archive packed alongside it
        ///     in the same table, for an edit that never happened.
        ///     <para>
        ///     <see cref="SetStream"/> clears it, because a replaced payload is by definition not
        ///     the one behind the stored bytes. The flag can therefore only ever be too
        ///     pessimistic, and being wrong in that direction costs one needless re-encode -
        ///     whereas being wrong in the other direction would leave a real edit unwritten.
        ///     </para>
        /// </remarks>
        public bool PayloadIsAsStored { get; internal set; }

        /// <summary>Whether this container still holds decoded data.</summary>
        public bool HasData => stream != null;

        /// <summary>Releases decoded data to save memory, unless the container is dirty.</summary>
        public void ReleaseData() {
            if(!Dirty) {
                stream = null;
                archive = null;
            }
        }

        public RSContainer() {

        }

        public RSContainer(RSContainer container) {
            indexId = container.GetIndexId();
            id = container.GetId();
            length = container.GetLength();
            compressionType = container.GetCompressionType();
            version = container.GetVersion();
            decompressedLength = container.GetDecompressedLength();
            StoredEncrypted = container.StoredEncrypted;
        }

        public RSContainer(int indexId, int id, byte compressionType, JagStream stream, int version) {
            this.indexId = indexId;
            this.id = id;
            this.compressionType = compressionType;
            this.stream = stream;
            this.version = version;
        }

        /// <summary>
        /// Constructs a new <see cref="RSContainer"/> from the stream data
        /// </summary>
        /// <param name="stream">The raw container data.</param>
        /// <param name="xteaKey">Optional XTEA key used to decrypt the payload.</param>
        /// <returns>The new container, or <c>null</c> if <paramref name="stream"/> is null.</returns>
        public static RSContainer Decode(JagStream stream, int[] xteaKey = null) {
            if (stream == null)
                return null;

            RSContainer container = new RSContainer();

            container.SetCompressionType((byte) stream.ReadByte());

            String compressionName = "None";
            if (container.GetCompressionType() == RSConstants.BZIP2_COMPRESSION)
                compressionName = "BZIP2";
            if (container.GetCompressionType() == RSConstants.GZIP_COMPRESSION)
                compressionName = "GZIP";

            container.SetDataLength(stream.ReadInt());

            bool compressed = container.GetCompressionType() != RSConstants.NO_COMPRESSION;

            /* The encrypted region starts immediately after the compression type and the
               compressed length, and runs to the end of the payload. For a compressed
               container that means the uncompressed-length field is *inside* it, so the
               field cannot be read until the region has been deciphered - reading it first
               yields four bytes of ciphertext, and deciphering only the payload after it
               shifts every block by four bytes so nothing decrypts at all. */
            int encryptedLength = container.GetDataLength() + (compressed ? 4 : 0);
            byte[] block = stream.ReadBytes(encryptedLength);

            if (xteaKey != null) {
                JagStream t = new JagStream(block);
                XTEA.Decipher(t, 0, block.Length, xteaKey);
                block = t.ToArray();
            }

            byte[] payload;
            if (compressed) {
                container.SetDecompressedLength(ReadInt(block, 0));
                payload = new byte[container.GetDataLength()];
                Array.Copy(block, 4, payload, 0, payload.Length);
            }
            else {
                container.SetDecompressedLength(container.GetDataLength()); // not compressed so it will match exactly
                payload = block;
            }

            Debug("Data Length: " + container.GetDataLength(), LOG_DETAIL.ADVANCED);
            Debug("Compression type: " + compressionName, LOG_DETAIL.ADVANCED);
            Debug("Decompressed length: " + container.GetDecompressedLength(), LOG_DETAIL.ADVANCED);

            payload = container.GetCompressionType() switch {
                RSConstants.BZIP2_COMPRESSION => CompressionUtils.Bunzip2(payload, container.GetDecompressedLength()),
                RSConstants.GZIP_COMPRESSION => CompressionUtils.Gunzip(payload),
                RSConstants.NO_COMPRESSION => payload,
                _ => throw new IOException("Invalid compression type")
            };

            if (payload.Length != container.GetDecompressedLength())
                throw new IOException("Length mismatch. [ " + payload.Length + " != " + container.GetDecompressedLength() + " ]");

            /* Reaching here with a key means the key fit: the payload inflated and matched the
               length recorded inside the encrypted region. That is the only evidence anywhere
               that this archive is stored encrypted, so it is recorded now - the write path has
               no way to establish it later. */
            container.StoredEncrypted = xteaKey != null;

            container.SetStream(new JagStream(payload));

            /* The payload just handed over is, by construction, the one the stored bytes encode.
               Recording that here is what lets the write path recognise a save that changes
               nothing and leave those stored bytes - and the CRC taken over them - alone. */
            container.PayloadIsAsStored = true;

            container.SetVersion(stream.Remaining() >= 2 ? stream.ReadUnsignedShort() : -1);
            container.PrintInfo();
            return container;
        }

        /// <summary>
        ///     Encodes this container to the on‑disk binary representation.
        /// </summary>
        /// <remarks>
        ///     Revision 639 uses the modern JS5 container header format:
        ///     <c>compressionType</c> (1&nbsp;byte) followed by
        ///     <c>compressedLength</c> and then, when compression is used,
        ///     <c>uncompressedLength</c>. Earlier revisions swapped the two
        ///     length fields; maintaining this order ensures compatibility with
        ///     the 639 client.
        ///     The payload may be XTEA encrypted when
        ///     <paramref name="xteaKey"/> is supplied. Multi‑file archives must
        ///     already contain the cumulative length table.
        /// </remarks>
        /// <param name="xteaKey">Optional 4&nbsp;integer XTEA key. When
        /// <c>null</c> no encryption is performed.</param>
        /// <returns>The encoded container bytes.</returns>
        public JagStream Encode(int[] xteaKey = null) {
            Debug("Encoding RSContainer " + id + ", length " + GetStream().Length);

            JagStream stream = new JagStream();

            byte[] uncompressed = GetStream().ToArray();
            int uncompressedLen = uncompressed.Length;

            compressionType = GetCompressionType();
            byte[] data = compressionType switch {
                RSConstants.BZIP2_COMPRESSION => CompressionUtils.Bzip2(uncompressed),
                RSConstants.GZIP_COMPRESSION => CompressionUtils.Gzip(uncompressed),
                _ => uncompressed
            };

            int compressedLen = data.Length;

            Debug("Compressed " + uncompressedLen + " to : " + compressedLen);

            stream.WriteByte(compressionType);
            stream.WriteInteger(compressedLen);

            /* Mirror of Decode: the uncompressed-length field sits inside the encrypted
               region, so it has to be enciphered along with the payload rather than written
               out in the clear beside it. With no key the bytes are identical either way. */
            JagStream block = new JagStream();
            if (compressionType != RSConstants.NO_COMPRESSION)
                block.WriteInteger(uncompressedLen);
            block.Write(data, 0, data.Length);
            byte[] region = block.Flip().ToArray();

            if (xteaKey != null) {
                JagStream temp = new JagStream(region);
                Cache.Util.Crypto.XTEA.Encipher(temp, 0, region.Length, xteaKey);
                region = temp.ToArray();
            }

            //Write the compressed (and possibly encrypted) data
            stream.Write(region, 0, region.Length);

            PrintByteArray(data);

            //Write out the optional version value
            if (GetVersion() != -1)
                stream.WriteShort(GetVersion());

            Debug("\t\t\tENCODED Container, stream len: " + stream.Length);
            PrintInfo();

            //Finally, flip the buffer and return it
            return stream.Flip();
        }

        /// <summary>Reads a big-endian 32-bit integer out of a decrypted header block.</summary>
        private static int ReadInt(byte[] data, int offset) {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        public string GetCompressionString() {
            return GetCompressionType() switch {
                RSConstants.BZIP2_COMPRESSION => "BZIP2",
                RSConstants.GZIP_COMPRESSION => "GZIP",
                _ => "None"
            };
        }

        private int GetDecompressedLength() {
            return decompressedLength;
        }

        private void SetDecompressedLength(int decompressedLength) {
            this.decompressedLength = decompressedLength;
        }

        private void SetVersion(int version) {
            this.version = version;
        }

        private void SetDataLength(int length) {
            this.length = length;
        }

        private int GetDataLength() {
            return length;
        }

        private void PrintInfo() {
            Debug("\t\t\tCompression type: " + GetCompressionString()
                + (stream == null ? "" : ", streamlen: " + stream.Length)
                + ", datalen: " + GetDataLength()
                + ", version: " + GetVersion(),
            LOG_DETAIL.ADVANCED);
        }

        /// <summary>
        ///     Replaces the decoded payload, and with it any claim that the stored bytes still
        ///     describe this container.
        /// </summary>
        /// <remarks>
        ///     <see cref="PayloadIsAsStored"/> is cleared here rather than at each call site so
        ///     that a payload can never be swapped out behind the flag's back. The write path
        ///     re-asserts it once, immediately after the new payload has actually reached the
        ///     store.
        /// </remarks>
        /// <param name="stream">The new payload.</param>
        public void SetStream(JagStream stream) {
            this.stream = stream;
            PayloadIsAsStored = false;
        }

        private void SetCompressionType(byte compressionType) {
            this.compressionType = compressionType;
        }

        /// <summary>
        /// Returns the compression type for the container
        /// </summary>
        /// <returns>The container's compression type</returns>
        internal byte GetCompressionType() {
            return compressionType;
        }

        /// <summary>
        /// Returns the version for the container
        /// </summary>
        /// <returns>The container's version</returns>
        internal int GetVersion() {
            return version;
        }

        /// <summary>
        /// Return the stream associated with this container
        /// </summary>
        /// <returns>The container stream</returns>
        internal JagStream GetStream() {
            return stream;
        }

        internal RSArchive GetArchive() {
            return archive;
        }

        internal void SetArchive(RSArchive archive) {
            this.archive = archive;
        }

        internal void SetIndexId(int indexId) {
            this.indexId = indexId;
        }

        internal void SetId(int id) {
            this.id = id;
        }

        public int GetIndexId() {
            return indexId;
        }

        public int GetId() {
            return id;
        }

        public int GetLength() {
            return length;
        }
    }
}
