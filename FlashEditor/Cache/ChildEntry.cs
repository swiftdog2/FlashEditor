using System;

namespace FlashEditor.cache {
    internal class ChildEntry : Entry {
        internal int identifier = -1;
        private int hash;

        internal void SetNameHash(int hash) {
            this.hash = hash;
        }

        internal int GetNameHash() {
            return hash;
        }
    }
}
