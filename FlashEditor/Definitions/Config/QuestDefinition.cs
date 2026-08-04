using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     One entry of a quest's opcode 3 or opcode 4 list: a short key and two 32-bit values.
    /// </summary>
    /// <remarks>
    ///     The fields are named by width and position because nothing in the 637 client reads either
    ///     list back - <c>anIntArrayArray1659</c> and <c>anIntArrayArray1648</c> have no getter and no
    ///     caller. Naming them for what a quest requirement would plausibly hold would be a guess
    ///     presented as a decode.
    /// </remarks>
    public readonly struct QuestConditionEntry {
        /// <summary>The entry's leading unsigned short.</summary>
        public int Key { get; }

        /// <summary>The entry's first 32-bit value.</summary>
        public int First { get; }

        /// <summary>The entry's second 32-bit value.</summary>
        public int Second { get; }

        /// <summary>Records one entry.</summary>
        /// <param name="key">The leading short.</param>
        /// <param name="first">The first int.</param>
        /// <param name="second">The second int.</param>
        public QuestConditionEntry(int key, int first, int second) {
            Key = key;
            First = first;
            Second = second;
        }
    }

    /// <summary>
    ///     One entry of a quest's opcode 18 or opcode 19 list: three 32-bit values and a string.
    /// </summary>
    /// <remarks>
    ///     Neither list has a reader in the 637 client, and neither opcode occurs in either cache, so
    ///     the shape is taken from <c>Class220.java:124-135</c> and <c>:156-167</c> and the fields are
    ///     left unnamed for the same reason <see cref="QuestConditionEntry"/>'s are.
    /// </remarks>
    public readonly struct QuestTextEntry {
        /// <summary>The entry's first 32-bit value.</summary>
        public int First { get; }

        /// <summary>The entry's second 32-bit value.</summary>
        public int Second { get; }

        /// <summary>The entry's third 32-bit value.</summary>
        public int Third { get; }

        /// <summary>The entry's trailing string.</summary>
        public string Text { get; }

        /// <summary>Records one entry.</summary>
        /// <param name="first">The first int.</param>
        /// <param name="second">The second int.</param>
        /// <param name="third">The third int.</param>
        /// <param name="text">The trailing string.</param>
        public QuestTextEntry(int first, int second, int third, string text) {
            First = first;
            Second = second;
            Third = third;
            Text = text ?? "";
        }
    }

    /// <summary>
    ///     A quest: its name, its requirement lists, and the sprite the chat line beside a name draws
    ///     for it.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.Quest"/>. Decoded by <c>Class220.method2816</c>
    ///     (:56-77) dispatching to <c>method2818</c> (:79-208); the provider is <c>Class13</c>, which
    ///     names the group at Class13.java:123.
    ///     <para>
    ///     <b>Settled by the data, not by a name.</b> Opcode 1's strings decode to "Cook's Assistant",
    ///     "Witch's House", "Priest in Peril" and so on, identically in both caches, so the group is
    ///     the quest table and opcode 1 is the quest name. That is corroborated by the join: item
    ///     definition opcode 132 already decodes to <c>ItemDefinition.quests</c> in this editor, and
    ///     the array it fills is the same <c>anIntArray2436</c> that <c>Class64_Sub25.method653</c>
    ///     (:9-38) walks, looking each id up in this group.
    ///     </para>
    ///     <para>
    ///     <b>Only one field is read back by this client.</b> <see cref="IconSpriteId"/> is turned
    ///     into an inline <c>&lt;img=n&gt;</c> tag on a chat line; every other field is decoded into a
    ///     private array with no getter and no caller. The rest are therefore recorded rather than
    ///     named.
    ///     </para>
    ///     <para>
    ///     <b>Opcodes 11 and 16 are absent from the dispatcher entirely</b>, so the client consumes no
    ///     payload for them and mis-reads everything after - the same defect floor overlay opcodes 4,
    ///     6 and 15 have. This decoder refuses them.
    ///     </para>
    ///     <para>
    ///     <c>Class13.method220</c> calls <c>method2819</c> after every decode, which copies
    ///     <see cref="Name"/> into <see cref="AlternateName"/> when opcode 2 was absent. That is a
    ///     post-decode transform rather than part of the format and is deliberately not done here:
    ///     applying it would make the encoder write opcode 2 into the 183 records that do not carry
    ///     it.
    ///     </para>
    /// </remarks>
    public sealed class QuestDefinition : ConfigDefinition {
        /// <summary>Opcode 1. The quest's name.</summary>
        /// <remarks>
        ///     <c>aString1663</c>, stored as a <c>gjstr2</c> so the leading zero version byte is part
        ///     of the payload. Carried by every record in both caches.
        /// </remarks>
        public string? Name { get; set; }

        /// <summary>Opcode 2. A second name the client falls back to <see cref="Name"/> for.</summary>
        /// <remarks>
        ///     <c>aString1654</c>, also a <c>gjstr2</c>. Four records carry it. Nothing in the 637
        ///     client reads it back, so what distinguishes it from <see cref="Name"/> is not settled.
        /// </remarks>
        public string? AlternateName { get; set; }

        /// <summary>Opcode 3. A keyed list of int pairs.</summary>
        /// <remarks><c>anIntArrayArray1659</c>. Carried by 73 records.</remarks>
        public List<QuestConditionEntry> Conditions3 { get; } = new List<QuestConditionEntry>();

        /// <summary>Opcode 4. A second keyed list of int pairs, the same shape as opcode 3's.</summary>
        /// <remarks><c>anIntArrayArray1648</c>. Carried by 113 records.</remarks>
        public List<QuestConditionEntry> Conditions4 { get; } = new List<QuestConditionEntry>();

        /// <summary>Opcode 5. An unsigned short the client reads and discards.</summary>
        /// <remarks>
        ///     Class220.java:186 calls <c>readUnsignedShort()</c> and assigns it to nothing, so there
        ///     is no field to reconstruct the bytes from and the value has to be kept verbatim.
        ///     Carried by 9 records.
        /// </remarks>
        public int Unknown5 { get; set; } = -1;

        /// <summary>Opcode 6. A byte the client reads and discards.</summary>
        /// <remarks>Class220.java:183. Carried by 5 records.</remarks>
        public int Unknown6 { get; set; } = -1;

        /// <summary>Opcode 7. A byte the client reads and discards.</summary>
        /// <remarks>Class220.java:180. Carried by 5 records.</remarks>
        public int Unknown7 { get; set; } = -1;

        /// <summary>Opcode 8. A bare flag; the client's dispatcher has an empty arm for it.</summary>
        /// <remarks>
        ///     Class220.java:96 consumes no payload and sets no field, so presence is its whole
        ///     content. Carried by 3 records.
        /// </remarks>
        public bool Unknown8 { get; set; }

        /// <summary>Opcode 9. A byte the client reads and discards.</summary>
        /// <remarks>Class220.java:176. Carried by 183 of the 187 records, the most common opcode after the name.</remarks>
        public int Unknown9 { get; set; } = -1;

        /// <summary>Opcode 10. A byte-counted list of 32-bit values.</summary>
        /// <remarks><c>anIntArray1652</c>. Carried by one record.</remarks>
        public int[]? Unknown10 { get; set; }

        /// <summary>Opcode 12. A 32-bit value the client reads and discards.</summary>
        /// <remarks>Class220.java:173. Occurs in no file of either cache.</remarks>
        public int Unknown12 { get; set; }

        /// <summary>Opcode 13. A byte-counted list of unsigned shorts.</summary>
        /// <remarks><c>anIntArray1651</c>. Carried by one record.</remarks>
        public int[]? Unknown13 { get; set; }

        /// <summary>Opcode 14. A byte-counted list of byte pairs, flattened in stored order.</summary>
        /// <remarks>
        ///     <c>anIntArrayArray1658</c>, two unsigned bytes per entry. Held flat, two entries per
        ///     pair, because nothing here settles what either byte is. Carried by two records.
        /// </remarks>
        public int[]? Unknown14 { get; set; }

        /// <summary>Opcode 15. An unsigned short the client reads and discards.</summary>
        /// <remarks>Class220.java:170. Occurs in no file of either cache.</remarks>
        public int Unknown15 { get; set; } = -1;

        /// <summary>Opcode 17. The sprite group in JS5 index 8 the chat icon is drawn from.</summary>
        /// <remarks>
        ///     <c>anInt1649</c>, and the only field of this record the 637 client reads back.
        ///     <c>Class64_Sub25.method653</c> (:9-38) loads it out of
        ///     <c>Class332_Sub2.aJS5Archive_5423</c>, which InterfaceSettings.java names as index 8,
        ///     and appends an <c>&lt;img=n&gt;</c> tag for it to a chat line. Carried by 21 records,
        ///     with values 836 to 4581 against index 8's 4,593 groups.
        /// </remarks>
        public int IconSpriteId { get; set; } = -1;

        /// <summary>Opcode 18. A byte-counted list of three ints and a string.</summary>
        /// <remarks>
        ///     <c>anIntArray1647/1653/1661</c> and <c>aStringArray1662</c>. Occurs in no file of
        ///     either cache.
        /// </remarks>
        public List<QuestTextEntry> Unknown18 { get; } = new List<QuestTextEntry>();

        /// <summary>Opcode 19. A second list of the same shape as opcode 18's.</summary>
        /// <remarks>
        ///     <c>anIntArray1646/1655/1660</c> and <c>aStringArray1656</c>. Occurs in no file of
        ///     either cache.
        /// </remarks>
        public List<QuestTextEntry> Unknown19 { get; } = new List<QuestTextEntry>();

        /// <summary>Opcode 249. The parameter block, in stored order.</summary>
        /// <remarks><c>aRSArray_1650</c>. Carried by one record.</remarks>
        public List<ConfigParameter> Parameters { get; } = new List<ConfigParameter>();

        /// <summary>Decodes one quest definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public QuestDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: Name = ConfigText.ReadVersionedString(stream); break;
                case 2: AlternateName = ConfigText.ReadVersionedString(stream); break;
                case 3: ReadConditions(stream, Conditions3); break;
                case 4: ReadConditions(stream, Conditions4); break;
                case 5: Unknown5 = stream.ReadUnsignedShort(); break;
                case 6: Unknown6 = stream.ReadUnsignedByte(); break;
                case 7: Unknown7 = stream.ReadUnsignedByte(); break;
                case 8: Unknown8 = true; break;
                case 9: Unknown9 = stream.ReadUnsignedByte(); break;

                case 10: {
                    int[] values = new int[stream.ReadUnsignedByte()];
                    for (int i = 0; i < values.Length; i++)
                        values[i] = stream.ReadInt();
                    Unknown10 = values;
                    break;
                }

                case 12: Unknown12 = stream.ReadInt(); break;

                case 13: {
                    int[] values = new int[stream.ReadUnsignedByte()];
                    for (int i = 0; i < values.Length; i++)
                        values[i] = stream.ReadUnsignedShort();
                    Unknown13 = values;
                    break;
                }

                case 14: {
                    int[] values = new int[stream.ReadUnsignedByte() * 2];
                    for (int i = 0; i < values.Length; i++)
                        values[i] = stream.ReadUnsignedByte();
                    Unknown14 = values;
                    break;
                }

                case 15: Unknown15 = stream.ReadUnsignedShort(); break;
                case 17: IconSpriteId = stream.ReadUnsignedShort(); break;
                case 18: ReadTextEntries(stream, Unknown18); break;
                case 19: ReadTextEntries(stream, Unknown19); break;
                case 249: ConfigParameters.Read(stream, Parameters); break;

                default:
                    //Opcodes 11 and 16 reach here: the client's dispatcher names neither, so it
                    //consumes nothing for them and reads the next payload byte as an opcode.
                    //Refusing turns that silent desync into a failure.
                    throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: ConfigText.WriteVersionedString(stream, Name ?? ""); break;
                case 2: ConfigText.WriteVersionedString(stream, AlternateName ?? ""); break;
                case 3: WriteConditions(stream, Conditions3); break;
                case 4: WriteConditions(stream, Conditions4); break;
                case 5: stream.WriteShort(Unknown5); break;
                case 6: stream.WriteByte(Unknown6); break;
                case 7: stream.WriteByte(Unknown7); break;
                case 8: break;
                case 9: stream.WriteByte(Unknown9); break;

                case 10: {
                    int[] values = Unknown10 ?? Array.Empty<int>();
                    stream.WriteByte(values.Length);
                    foreach (int value in values)
                        stream.WriteInteger(value);
                    break;
                }

                case 12: stream.WriteInteger(Unknown12); break;

                case 13: {
                    int[] values = Unknown13 ?? Array.Empty<int>();
                    stream.WriteByte(values.Length);
                    foreach (int value in values)
                        stream.WriteShort(value);
                    break;
                }

                case 14: {
                    int[] values = Unknown14 ?? Array.Empty<int>();
                    if ((values.Length & 1) != 0)
                        throw new System.IO.InvalidDataException("Quest " + Id + " has " +
                            values.Length + " opcode 14 bytes; they are pairs.");
                    stream.WriteByte(values.Length / 2);
                    foreach (int value in values)
                        stream.WriteByte(value);
                    break;
                }

                case 15: stream.WriteShort(Unknown15); break;
                case 17: stream.WriteShort(IconSpriteId); break;
                case 18: WriteTextEntries(stream, Unknown18); break;
                case 19: WriteTextEntries(stream, Unknown19); break;
                case 249: ConfigParameters.Write(stream, Parameters); break;

                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && Name != null) yield return 1;
            if (!Has(2) && AlternateName != null) yield return 2;
            if (!Has(3) && Conditions3.Count > 0) yield return 3;
            if (!Has(4) && Conditions4.Count > 0) yield return 4;
            if (!Has(5) && Unknown5 != -1) yield return 5;
            if (!Has(6) && Unknown6 != -1) yield return 6;
            if (!Has(7) && Unknown7 != -1) yield return 7;
            if (!Has(8) && Unknown8) yield return 8;
            if (!Has(9) && Unknown9 != -1) yield return 9;
            if (!Has(10) && Unknown10 != null) yield return 10;
            if (!Has(12) && Unknown12 != 0) yield return 12;
            if (!Has(13) && Unknown13 != null) yield return 13;
            if (!Has(14) && Unknown14 != null) yield return 14;
            if (!Has(15) && Unknown15 != -1) yield return 15;
            if (!Has(17) && IconSpriteId != -1) yield return 17;
            if (!Has(18) && Unknown18.Count > 0) yield return 18;
            if (!Has(19) && Unknown19.Count > 0) yield return 19;
            if (!Has(249) && Parameters.Count > 0) yield return 249;
        }

        /// <summary>Reads a byte-counted list of (short, int, int) entries.</summary>
        /// <param name="stream">The definition file, positioned at the count.</param>
        /// <param name="into">The list to fill, cleared first.</param>
        private static void ReadConditions(JagStream stream, List<QuestConditionEntry> into) {
            into.Clear();

            int count = stream.ReadUnsignedByte();
            for (int i = 0; i < count; i++)
                into.Add(new QuestConditionEntry(stream.ReadUnsignedShort(), stream.ReadInt(),
                    stream.ReadInt()));
        }

        /// <summary>Writes a byte-counted list of (short, int, int) entries.</summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="entries">The entries, in stored order.</param>
        private static void WriteConditions(JagStream stream, List<QuestConditionEntry> entries) {
            stream.WriteByte(entries.Count);
            foreach (QuestConditionEntry entry in entries) {
                stream.WriteShort(entry.Key);
                stream.WriteInteger(entry.First);
                stream.WriteInteger(entry.Second);
            }
        }

        /// <summary>Reads a byte-counted list of (int, int, int, string) entries.</summary>
        /// <param name="stream">The definition file, positioned at the count.</param>
        /// <param name="into">The list to fill, cleared first.</param>
        private static void ReadTextEntries(JagStream stream, List<QuestTextEntry> into) {
            into.Clear();

            int count = stream.ReadUnsignedByte();
            for (int i = 0; i < count; i++)
                into.Add(new QuestTextEntry(stream.ReadInt(), stream.ReadInt(), stream.ReadInt(),
                    stream.ReadJagexString()));
        }

        /// <summary>Writes a byte-counted list of (int, int, int, string) entries.</summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="entries">The entries, in stored order.</param>
        private static void WriteTextEntries(JagStream stream, List<QuestTextEntry> entries) {
            stream.WriteByte(entries.Count);
            foreach (QuestTextEntry entry in entries) {
                stream.WriteInteger(entry.First);
                stream.WriteInteger(entry.Second);
                stream.WriteInteger(entry.Third);
                stream.WriteJagexString(entry.Text);
            }
        }
    }
}
