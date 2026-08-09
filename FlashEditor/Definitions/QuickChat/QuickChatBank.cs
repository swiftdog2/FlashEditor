using System;
using FlashEditor.Cache;

namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     How the two quick-chat indexes are laid out and how a global quick-chat id picks between
    ///     them.
    /// </summary>
    /// <remarks>
    ///     Indexes 24 and 25 are <b>two id namespaces of one format</b>, not a menu index and a
    ///     message index - the constant names <c>QUICK_CHAT_MESSAGES</c> and <c>QUICK_CHAT_MENU</c>
    ///     describe a split that does not exist. Each index holds both halves, separated by group:
    ///     group 0 is the menu tree and group 1 the message templates.
    ///     <para>
    ///     Settled from the client, which has exactly one reader per format and hands it both
    ///     archives. <c>Class212</c> is the menu loader - built at InterfaceSettings.java:297-298
    ///     with index 24 and index 25 - and reads folder 0 of whichever it picks
    ///     (Class212.java:66,71), decoding through <c>Node_Sub46_Sub1.method1532</c> in both cases.
    ///     <c>Class280</c> is the message loader, built at :299-300 from the same pair, reads
    ///     folder 1 (Class280.java:378,383) and decodes through
    ///     <c>Node_Sub46_Sub11.method1584</c>. One decoder, two archives, so the formats are shared
    ///     by construction rather than by resemblance.
    ///     </para>
    ///     <para>
    ///     Neither table carries name hashes, so a record is addressable only by id - and the ids are
    ///     not dense. Index 25's message group spans 0 to 69 with 62 absent, in both caches, so file
    ///     ids have to come from the reference table rather than from a <c>0..count-1</c> walk, which
    ///     would ask for a file that does not exist and miss the one that does.
    ///     </para>
    /// </remarks>
    public static class QuickChatBank {
        /// <summary>
        ///     The group holding menu records, in either index.
        /// </summary>
        /// <remarks>Folder 0, at Class212.java:66,71.</remarks>
        public const int MenuGroup = 0;

        /// <summary>
        ///     The group holding message records, in either index.
        /// </summary>
        /// <remarks>Folder 1, at Class280.java:378,383.</remarks>
        public const int MessageGroup = 1;

        /// <summary>
        ///     The bit that sends a quick-chat id to the second bank.
        /// </summary>
        /// <remarks>
        ///     A lookup takes index 25 with the id masked to <c>id &amp; 0x7fff</c> when the id is at
        ///     least this, and index 24 with the id unchanged otherwise
        ///     (Class212.java:65-66, Class280.java:377-378). Records loaded from index 25 then have
        ///     the bit OR'd back onto every id they store (Node_Sub46_Sub1.method1531,
        ///     Node_Sub46_Sub11.method1575), so index 25's id graph is self-contained and the stored
        ///     ids never carry it.
        /// </remarks>
        public const int SecondBankFlag = 0x8000;

        /// <summary>
        ///     Which index a global quick-chat id addresses.
        /// </summary>
        /// <param name="globalId">The id as the client sees it, second-bank bit included.</param>
        /// <returns>24 or 25.</returns>
        public static int IndexOf(int globalId) {
            Require(globalId);
            return (globalId & SecondBankFlag) != 0
                ? RSConstants.QUICK_CHAT_MENU
                : RSConstants.QUICK_CHAT_MESSAGES;
        }

        /// <summary>
        ///     The file id a global quick-chat id names within its own index.
        /// </summary>
        /// <param name="globalId">The id as the client sees it, second-bank bit included.</param>
        /// <returns>The stored file id.</returns>
        public static int FileIdOf(int globalId) {
            Require(globalId);
            return globalId & (SecondBankFlag - 1);
        }

        /// <summary>
        ///     The global id a record's stored id carries, given which index it was read from.
        /// </summary>
        /// <remarks>
        ///     The inverse of <see cref="IndexOf"/> and <see cref="FileIdOf"/>. An editor that shows
        ///     global ids must fold through this rather than displaying the stored value, or the
        ///     two banks' records appear to reference each other.
        /// </remarks>
        /// <param name="indexId">The index the record was read from, 24 or 25.</param>
        /// <param name="storedId">The id as the record stores it, without the second-bank bit.</param>
        /// <returns>The global id.</returns>
        public static int GlobalId(int indexId, int storedId) {
            if (storedId < 0 || storedId >= SecondBankFlag)
                throw new ArgumentOutOfRangeException(nameof(storedId), storedId,
                    "A stored quick-chat id is below 0x8000; the second-bank bit is added at load, " +
                    "never stored.");

            if (indexId == RSConstants.QUICK_CHAT_MESSAGES)
                return storedId;
            if (indexId == RSConstants.QUICK_CHAT_MENU)
                return storedId | SecondBankFlag;

            throw new ArgumentOutOfRangeException(nameof(indexId), indexId,
                "Only indexes 24 and 25 hold quick-chat banks.");
        }

        private static void Require(int globalId) {
            if (globalId < 0)
                throw new ArgumentOutOfRangeException(nameof(globalId), globalId,
                    "Quick-chat ids are non-negative.");
        }
    }
}
