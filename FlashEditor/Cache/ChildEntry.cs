using System;

namespace FlashEditor.cache {
    internal class ChildEntry : Entry {
        int index;

        public ChildEntry(JagStream stream) : base(stream) {

        }

        public ChildEntry(int index) {
            this.index = index;
        }
    }
}
