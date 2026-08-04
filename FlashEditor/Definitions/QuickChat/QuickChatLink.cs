namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     One entry of a menu record's submenu or message list: what it points at, and the key
    ///     that selects it.
    /// </summary>
    /// <remarks>
    ///     Both of a menu's lists (opcodes 2 and 3) store the same pair, read at
    ///     Node_Sub46_Sub1.java:44-49 and :54-58.
    /// </remarks>
    public sealed class QuickChatLink {
        /// <summary>Points a list entry at a record, with the key that selects it.</summary>
        /// <param name="targetId">The stored target id.</param>
        /// <param name="shortcut">The stored shortcut byte.</param>
        public QuickChatLink(int targetId, byte shortcut) {
            TargetId = targetId;
            Shortcut = shortcut;
        }

        /// <summary>
        ///     The record this entry points at, as the file stores it.
        /// </summary>
        /// <remarks>
        ///     Without the second-bank bit. A record read from index 25 has
        ///     <see cref="QuickChatBank.SecondBankFlag"/> OR'd onto every one of these at load
        ///     (Node_Sub46_Sub1.method1531), so the stored value is always below 0x8000 and the
        ///     index it belongs to is context, not content.
        /// </remarks>
        public int TargetId { get; set; }

        /// <summary>
        ///     The keyboard shortcut byte, kept as stored rather than as the character it displays.
        /// </summary>
        /// <remarks>
        ///     The client maps it through the same modified cp1252 as a string
        ///     (<c>Class64_Sub7.method576</c>, Class64_Sub7.java:9-31), which folds the five
        ///     unassigned bytes in the 0x80-0x9F band onto a question mark - so the character cannot
        ///     be turned back into the byte. Every shortcut in both caches is plain ASCII, which is
        ///     exactly why the byte is kept: nothing in the data would catch the loss.
        ///     <para>Zero means no shortcut (Node_Sub46_Sub1.java:47-48,57).</para>
        /// </remarks>
        public byte Shortcut { get; set; }

        /// <summary>The character the shortcut byte displays as, or NUL when there is none.</summary>
        public char ShortcutChar => Shortcut == 0 ? '\0' : QuickChatText.ToText(new[] { Shortcut })[0];
    }
}
