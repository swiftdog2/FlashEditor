using static FlashEditor.Utils.DebugUtil;
using System;
using System.Collections.Generic;

namespace FlashEditor.cache {
    public class RSArchiveEntry {
        public int identifier = -1;
        public int hash;
        public byte[] whirlpool = new byte[64];
        public int crc;
        public int version;
        public int id;

        /// <summary>Bit 0 of the format-7 archive-flags byte: the XTEA marker.</summary>
        public const byte FLAG_XTEA = 0x01;

        /// <summary>
        /// The raw per-archive flags byte read from a format-7 reference table. Only bit 0
        /// (<see cref="FLAG_XTEA"/>) has a known meaning here, so the whole byte is kept
        /// rather than rebuilt from what is understood - the table is re-encoded on every
        /// edit, and any bit dropped on the way out is gone from the cache for good.
        /// </summary>
        public byte ArchiveFlags { get; set; }

        /// <summary>
        /// Whether the archive is XTEA-encrypted. A view over bit 0 of
        /// <see cref="ArchiveFlags"/>, so setting it leaves every other bit untouched.
        /// </summary>
        public bool UsesXtea {
            get => (ArchiveFlags & FLAG_XTEA) != 0;
            set => ArchiveFlags = (byte) (value ? ArchiveFlags | FLAG_XTEA : ArchiveFlags & ~FLAG_XTEA);
        }


        private SortedDictionary<int, RSFileEntry> fileEntries = new SortedDictionary<int, RSFileEntry>();

        /// <summary>
        ///     The file ids the reference table declared for this archive, or <c>null</c> for an
        ///     entry that never came from one.
        /// </summary>
        /// <remarks>
        ///     Nullable rather than defaulted to an empty array on purpose. An empty list is a
        ///     legitimate decoded value - a reference table really can declare an archive with no
        ///     files - so defaulting to <c>Array.Empty</c> would make "never decoded" read as
        ///     "decoded as empty", which is the one thing <see cref="GetValidFileIds"/> exists to
        ///     tell apart. Every inherited <see cref="RSFileEntry"/> leaves it null, a file entry
        ///     having no files of its own.
        /// </remarks>
        private int[]? validFileIds;

        public int compressed;
        public int uncompressed;

        public RSArchiveEntry(int id) {
            this.id = id;
        }

        public RSArchiveEntry() {
        }

        public int GetIdentifier() {
            return identifier;
        }

        public void SetIdentifier(int identifier) {
            this.identifier = identifier;
        }

        public int GetCrc() {
            return crc;
        }

        public void SetCrc(int crc) {
            this.crc = crc;
        }

        public byte[] GetWhirlpool() {
            return whirlpool;
        }

        public void SetWhirlpool(ReadOnlySpan<byte> whirlpool) {
            if(whirlpool.Length != 64) {
                Debug("Whirlpool length is not 64 bytes");
                throw new ArgumentException();
            }
            whirlpool.CopyTo(this.whirlpool);
        }

        public int GetVersion() {
            return version;
        }

        public void SetVersion(int version) {
            this.version = version;
        }

        public void PutFileEntry(int fileId, RSFileEntry entry) {
            fileEntries.Add(fileId, entry);
        }

        public SortedDictionary<int, RSFileEntry> GetFileEntries() {
            return fileEntries;
        }

        public void SetHash(int hash) {
            this.hash = hash;
        }

        public long GetHash() {
            return hash;
        }

        public void SetValidFileIds(int[] validFileIds) {
            this.validFileIds = validFileIds;
        }

        /// <summary>
        ///     The file ids the reference table declared for this archive, ascending.
        /// </summary>
        /// <remarks>
        ///     This is the list <see cref="RSArchive.Decode"/> is driven by, so it has to describe
        ///     what the stored payload was encoded against rather than what the archive happens to
        ///     hold now.
        ///     <para>
        ///     An entry that never went through a reference table has no list, and every caller
        ///     walks or measures the result immediately. Handing back a null would surface that as
        ///     a <see cref="NullReferenceException"/> a frame or two away with nothing naming the
        ///     entry, so the missing list is reported here instead.
        ///     </para>
        /// </remarks>
        /// <returns>The declared file ids.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The entry was never given a file id list - it did not come from a decoded
        ///     reference table, and nothing called <see cref="SetValidFileIds"/> on it.
        /// </exception>
        public int[] GetValidFileIds() {
            return validFileIds ?? throw new InvalidOperationException(
                "Archive entry " + id + " has no valid file id list - it was not decoded from a reference table.");
        }

        public void SetFileEntries(SortedDictionary<int, RSFileEntry> fileEntries) {
            this.fileEntries = fileEntries;
        }

        /// <summary>
        ///     The metadata entry for a file, or <c>null</c> when the archive does not list it.
        /// </summary>
        /// <remarks>
        ///     Absence is an ordinary answer here rather than a failure: the write path asks
        ///     whether a file already has an entry before creating one, so the null return is the
        ///     signal and its callers test for it.
        /// </remarks>
        /// <param name="fileId">The file id to look up.</param>
        /// <returns>The entry, or <c>null</c> when the file is not listed.</returns>
        public RSFileEntry? GetFileEntry(int fileId) {
            if(!GetFileEntries().ContainsKey(fileId))
                return null;
            return GetFileEntries()[fileId];
        }
    }
}
