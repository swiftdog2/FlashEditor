using static FlashEditor.Utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache.CheckSum;

namespace FlashEditor.cache {
    public class RSArchiveEntry {
        private JagStream stream = new JagStream(); //ensure there is a default stream
        public int identifier = -1;
        public RSIdentifiers identifiers;
        public int hash;
        public byte[] whirlpool = new byte[64];
        public int crc;
        public int version;
        public int id;
        public bool UsesXtea { get; set; }  // true ⇢ groupFlags bit-0 is set


        private SortedDictionary<int, RSFileEntry> fileEntries = new SortedDictionary<int, RSFileEntry>();
        private int[] validFileIds;

        public int compressed;
        public int uncompressed;

        public RSArchiveEntry(int id) {
            this.id = id;
        }

        public RSArchiveEntry() {
        }

        public RSArchiveEntry(JagStream stream) {
            this.stream = stream;
        }

        public virtual int GetId() {
            return id;
        }

        public virtual int GetIdentifier() {
            return identifier;
        }

        public virtual void SetIdentifier(int identifier) {
            this.identifier = identifier;
        }

        public virtual int GetCrc() {
            return crc;
        }

        public virtual void SetCrc(int crc) {
            this.crc = crc;
        }

        public virtual byte[] GetWhirlpool() {
            return whirlpool;
        }

        public virtual void SetWhirlpool(ReadOnlySpan<byte> whirlpool) {
            if(whirlpool.Length != 64) {
                Debug("Whirlpool length is not 64 bytes");
                throw new ArgumentException();
            }
            whirlpool.CopyTo(this.whirlpool);
        }

        public JagStream GetStream() {
            return stream;
        }

        public virtual int GetVersion() {
            return version;
        }

        public virtual void SetVersion(int version) {
            this.version = version;
        }

        public virtual int GetSize() {
            return fileEntries.Count;
        }

        public virtual int Capacity() {
            if(fileEntries.Count == 0)
                return 0;

            return (int) fileEntries.Keys.Last() + 1;
        }

        public virtual void PutFileEntry(int fileId, RSFileEntry entry) {
            fileEntries.Add(fileId, entry);
        }

        public virtual void RemoveFileEntry(int fileId, RSFileEntry entry) {
            fileEntries.Remove(fileId);
        }

        public virtual SortedDictionary<int, RSFileEntry> GetFileEntries() {
            return fileEntries;
        }

        public void SetHash(int hash) {
            this.hash = hash;
        }

        // Computes a hash used for naming entries within the cache editor
        public int CalculateHash() {
            int h = 0;

            foreach(byte b in stream.ToArray())
                h = h * 31 + b;

            return h;
        }
        public long GetHash() {
            return hash;
        }

        public void SetValidFileIds(int[] validFileIds) {
            this.validFileIds = validFileIds;
        }

        public int[] GetValidFileIds() {
            return validFileIds;
        }

        public void SetFileEntries(SortedDictionary<int, RSFileEntry> fileEntries) {
            this.fileEntries = fileEntries;
        }

        public RSFileEntry GetFileEntry(int fileId) {
            if(!GetFileEntries().ContainsKey(fileId))
                return null;
            return GetFileEntries()[fileId];
        }
    }
}
