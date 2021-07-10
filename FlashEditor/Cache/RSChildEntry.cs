using System;

namespace FlashEditor.cache {
    internal class RSChildEntry : RSEntry {
        int index;

        public RSChildEntry(JagStream stream) : base(stream) {

        }

        public RSChildEntry(int index) {
            this.index = index;
        }
    }
}
