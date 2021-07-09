using System;
using System.Collections.Generic;
using System.Linq;

namespace FlashEditor.cache {
    internal class Entry {
        public JagStream stream = new JagStream(); //ensure there is a default stream
        internal int identifier = -1;
        internal int crc;
        public int hash;
        internal byte[] whirlpool = new byte[64];
        internal int version;
        int id;

        internal SortedDictionary<int?, ChildEntry> childEntries = new SortedDictionary<int?, ChildEntry>();
        private int[] validFileIds;
        internal int compressed;
        internal int uncompressed;

        public Entry(int id) {
            this.id = id;
        }

        public Entry() {
        }

        public Entry(JagStream stream) {
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

        public virtual int GetSize() {
            return childEntries.Count;
        }

        public virtual int Capacity() {
            if(childEntries.Count == 0)
                return 0;

            return (int) childEntries.Keys.Last() + 1;
        }

        public virtual ChildEntry GetEntry(int id) {
            return childEntries[id];
        }

        public virtual void PutEntry(int id, ChildEntry entry) {
            childEntries.Add(id, entry);
        }

        public virtual void RemoveEntry(int id, ChildEntry entry) {
            childEntries.Remove(id);
        }

        public virtual SortedDictionary<int?, ChildEntry> GetEntries() {
            return childEntries;
        }

        internal void SetNameHash(int hash) {
            this.hash = hash;
        }

        //Pretty sure this is for naming shit so you can find it in the cache editor tho lol sneaky jagex
        internal int CalculateNameHash() {
            int h = 0;

            foreach(byte b in stream.ToArray())
                h = h * 31 + b;

            return h;
        }

        internal void SetValidFileIds(int[] validFileIds) {
            this.validFileIds = validFileIds;
        }

        internal int[] GetValidFileIds() {
            return validFileIds;
        }

        internal void SetFiles(SortedDictionary<int?, ChildEntry> entries) {
            this.childEntries = entries;
        }

        internal long GetNameHash() {
            return hash;
        }
    }
}