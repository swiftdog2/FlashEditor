using System;
using System.Collections.Generic;
using System.Linq;

namespace FlashEditor.cache {
    internal class RSEntry {
        internal int identifier = -1;
        internal int crc;
        public int hash;
        internal byte[] whirlpool = new byte[64];
        internal int version;
        int index;

        internal SortedDictionary<int?, RSChildEntry> entries = new SortedDictionary<int?, RSChildEntry>();
        private int[] validFileIds;

        public RSEntry(int index) {
            this.index = index;
        }

        public virtual int GetIndex() {
            return index;
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

        public virtual void SetWhirlpool(byte[] whirlpool) {
            if(whirlpool.Length != 64)
                throw new ArgumentException("Whirlpool length is not 64 bytes");
            Array.Copy(whirlpool, 0, this.whirlpool, 0, whirlpool.Length);
        }

        public virtual int GetVersion() {
            return version;
        }

        public virtual void SetVersion(int version) {
            this.version = version;
        }

        public virtual int Size() {
            return entries.Count;
        }

        public virtual int Capacity() {
            if(entries.Count == 0)
                return 0;

            return (int) entries.Keys.Last() + 1;
        }

        public virtual RSChildEntry GetEntry(int id) {
            return entries[id];
        }

        public virtual void PutEntry(int id, RSChildEntry entry) {
            entries.Add(id, entry);
        }

        public virtual void RemoveEntry(int id, RSChildEntry entry) {
            entries.Remove(id);
        }

        public virtual SortedDictionary<int?, RSChildEntry> GetEntries() {
            return entries;
        }

        internal void SetNameHash(int hash) {
            this.hash = hash;
        }

        internal void SetValidFileIds(int[] validFileIds) {
            this.validFileIds = validFileIds;
        }

        internal int[] GetValidFileIds() {
            return validFileIds;
        }

        internal void SetFiles(SortedDictionary<int?, RSChildEntry> entries) {
            this.entries = entries;
        }

        internal long GetNameHash() {
            return hash;
        }
    }
}