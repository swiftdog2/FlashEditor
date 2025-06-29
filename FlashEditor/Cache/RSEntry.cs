﻿using static FlashEditor.Utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache.CheckSum;

namespace FlashEditor.cache {
    public class RSEntry {
        private JagStream stream = new JagStream(); //ensure there is a default stream
        public int identifier = -1;
        public RSIdentifiers identifiers;
        public int hash;
        public byte[] whirlpool = new byte[64];
        public int crc;
        public int version;
        public int id;
        public bool UsesXtea { get; set; }  // true ⇢ groupFlags bit-0 is set


        private SortedDictionary<int, RSChildEntry> childEntries = new SortedDictionary<int, RSChildEntry>();
        private int[] validFileIds;

        public int compressed;
        public int uncompressed;

        public RSEntry(int id) {
            this.id = id;
        }

        public RSEntry() {
        }

        public RSEntry(JagStream stream) {
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
            return childEntries.Count;
        }

        public virtual int Capacity() {
            if(childEntries.Count == 0)
                return 0;

            return (int) childEntries.Keys.Last() + 1;
        }

        public virtual void PutEntry(int id, RSChildEntry entry) {
            childEntries.Add(id, entry);
        }

        public virtual void RemoveEntry(int id, RSChildEntry entry) {
            childEntries.Remove(id);
        }

        public virtual SortedDictionary<int, RSChildEntry> GetChildEntries() {
            return childEntries;
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

        public void SetChildEntries(SortedDictionary<int, RSChildEntry> childEntries) {
            this.childEntries = childEntries;
        }

        public RSChildEntry GetChildEntry(int member) {
            if(!GetChildEntries().ContainsKey(member))
                return null;
            return GetChildEntries()[member];
        }
    }
}