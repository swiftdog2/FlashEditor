using System;

namespace FlashEditor.cache {
    public class RSChildEntry {
        internal int identifier = -1;
        private int hash;

        internal void SetNameHash(int hash) {
            this.hash = hash;
        }
    }
}
