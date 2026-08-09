using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     One CS2 script from index 12 as a list row.
    /// </summary>
    /// <remarks>
    ///     The row carries the whole decoded script rather than a summary of it, which is what lets
    ///     the instruction pane beside the list cost no cache read at all. It is also what the
    ///     four editable counts need: <see cref="ClientScriptDefinition.Encode"/> writes the entire
    ///     record, so a row that had thrown its instructions away could not write one back.
    ///     <para>
    ///     <b>The stored length is a column because nothing else states it.</b> Index 12's reference
    ///     table sets neither the sizes flag nor whirlpool, so the only statement of how large a
    ///     script is comes from decompressing it. Both supported caches hold the same 4,149 scripts
    ///     in 2,554,245 decompressed bytes, and script 978 alone is 106,307 of them - about 170
    ///     times the mean - so a reader with no size column has no way to see that coming.
    ///     </para>
    /// </remarks>
    public sealed class ClientScriptListing {
        /// <summary>Binds one decoded script to where it came from.</summary>
        /// <param name="address">The group and file, and the script id they carry.</param>
        /// <param name="record">The decoded script.</param>
        /// <param name="identifier">The reference table's identifier for the group.</param>
        /// <param name="storedLength">The decompressed file's length in bytes.</param>
        public ClientScriptListing(DefinitionAddress address, ClientScriptDefinition record,
            int identifier, int storedLength) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
            Identifier = identifier;
            StoredLength = storedLength;
        }

        /// <summary>Where the script lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded script, which the instruction pane reads its rows from.</summary>
        public ClientScriptDefinition Record { get; }

        /// <summary>The script id, which on this index is the group id.</summary>
        public int ScriptId => Address.GroupId;

        /// <summary>
        ///     The identifier the reference table carries for this group, exactly as stored.
        /// </summary>
        /// <remarks>
        ///     <b>Not a name hash, and deliberately not presented as one.</b> The client feeds
        ///     <c>eventType | componentKey &lt;&lt; 10</c> into the identifier map at
        ///     <c>Class213.java:51,77,105</c>, so part of this population is a packed interface hook -
        ///     one script's identifier is exactly the <c>0x3fffc00</c> global-default literal OR-ed
        ///     with an event type, and a few dozen more sit in the
        ///     <c>(interfaceId + 65536) &lt;&lt; 10</c> window with consecutive interface ids. The
        ///     other roughly 3,800 are uniformly distributed 32-bit values with no structure anyone
        ///     has proven, and no script name has ever been recovered to test the
        ///     <c>String.hashCode</c> hypothesis against. Labelling the column "name hash" would be
        ///     the plausible-mapping-confirmed-by-accident failure this project has already paid for
        ///     once on the track-name join.
        ///     <para>
        ///     Shown raw, with no unnamed sentinel. <c>RSArchiveEntry</c> spells "no identifier" as
        ///     -1, and 1,929 of these 4,149 identifiers are negative in both supported caches, so a
        ///     negative value is ordinary here rather than a marker. None is exactly -1 today and
        ///     nothing in the format stops one being, so a row that suppressed -1 would hide a real
        ///     identifier the moment one appeared.
        ///     </para>
        /// </remarks>
        public int Identifier { get; }

        /// <summary>The identifier again in hex, where the packed hooks are readable.</summary>
        /// <remarks>
        ///     A packed hook is only recognisable in hex - the global-default one reads as
        ///     <c>0x03FFFC10</c> and as 67,105,296 in decimal - so both spellings are columns rather
        ///     than a choice between them.
        /// </remarks>
        public string IdentifierHex => "0x" + Identifier.ToString("X8");

        /// <summary>How many bytes the decompressed file occupies.</summary>
        public int StoredLength { get; }

        /// <summary>How many instructions the script holds.</summary>
        public int InstructionCount => Record.Instructions.Count;

        /// <summary>How many switch tables the script holds.</summary>
        public int SwitchBlockCount => Record.SwitchBlocks.Count;

        /// <summary>How many arms those switch tables hold in total.</summary>
        public int SwitchCaseCount {
            get {
                int cases = 0;
                foreach (ClientScriptSwitchBlock block in Record.SwitchBlocks)
                    cases += block.Cases.Count;
                return cases;
            }
        }

        /// <summary>The integer frame size, which is what the client allocates the callee's array at.</summary>
        public int IntegerLocalCount {
            get => Record.IntegerLocalCount;
            set => Record.IntegerLocalCount = value;
        }

        /// <summary>The string frame size.</summary>
        public int StringLocalCount {
            get => Record.StringLocalCount;
            set => Record.StringLocalCount = value;
        }

        /// <summary>How many integers a caller pushes for this script.</summary>
        public int IntegerParameterCount {
            get => Record.IntegerParameterCount;
            set => Record.IntegerParameterCount = value;
        }

        /// <summary>How many strings a caller pushes for this script.</summary>
        public int StringParameterCount {
            get => Record.StringParameterCount;
            set => Record.StringParameterCount = value;
        }

        /// <summary>The optional leading name, or a note that the record carries none.</summary>
        /// <remarks>
        ///     Not a column: no script in either supported cache stores one, so a column would be
        ///     4,149 blank cells. It is on the summary line instead, where "absent" is a statement
        ///     rather than an empty box.
        /// </remarks>
        public string NameOrAbsent => Record.Name == null ? "absent" : "\"" + Record.Name + "\"";
    }

    /// <summary>
    ///     Index 12 as a definition list: one row per compiled CS2 script.
    /// </summary>
    /// <remarks>
    ///     One script per group and one file per group, so a script id is a group id - both client
    ///     readers land on <c>getChildFromFolder(id, 0)</c>. The base class already walks the
    ///     reference table, so the two groups the repack leaves in its idx file and in no table are
    ///     absent from this list for the same reason the client cannot load them.
    ///     <para>
    ///     <b>Editable, but only in the four footer counts.</b> They are the record's single-valued
    ///     fields and <see cref="ClientScriptDefinition.Encode"/> derives everything around them, so
    ///     an edit to one rewrites that field and nothing else. The instruction stream is not a cell:
    ///     it is a variable-length list of opcode/operand pairs whose widths follow from the opcodes,
    ///     and it belongs in the pane beside the list. Nothing here offers to edit an opcode, because
    ///     without a disassembler a user changing one has no way to know what it does.
    ///     </para>
    /// </remarks>
    public sealed class ClientScriptListDescriptor : DefinitionListDescriptor<ClientScriptListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every script the index declares.</summary>
        public ClientScriptListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<ClientScriptListing>("Script", row => row.ScriptId, 70),
                DefinitionColumn.ReadOnly<ClientScriptListing>("Identifier", row => row.Identifier, 120),
                DefinitionColumn.ReadOnly<ClientScriptListing>("Identifier hex", row => row.IdentifierHex, 120),
                DefinitionColumn.ReadOnly<ClientScriptListing>("Bytes", row => row.StoredLength, 80),
                DefinitionColumn.ReadOnly<ClientScriptListing>("Instructions", row => row.InstructionCount, 100),
                DefinitionColumn.Number<ClientScriptListing>("Int locals", row => row.IntegerLocalCount,
                    (row, value) => row.IntegerLocalCount = Storable(value, row.IntegerLocalCount), 90),
                DefinitionColumn.Number<ClientScriptListing>("Str locals", row => row.StringLocalCount,
                    (row, value) => row.StringLocalCount = Storable(value, row.StringLocalCount), 90),
                DefinitionColumn.Number<ClientScriptListing>("Int params", row => row.IntegerParameterCount,
                    (row, value) => row.IntegerParameterCount = Storable(value, row.IntegerParameterCount), 90),
                DefinitionColumn.Number<ClientScriptListing>("Str params", row => row.StringParameterCount,
                    (row, value) => row.StringParameterCount = Storable(value, row.StringParameterCount), 90),
                DefinitionColumn.ReadOnly<ClientScriptListing>("Switches", row => row.SwitchBlockCount, 80),
                DefinitionColumn.ReadOnly<ClientScriptListing>("Cases", row => row.SwitchCaseCount, 70)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.CLIENT_SCRIPTS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "client script";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <summary>
        ///     Decodes one script and joins the reference table's identifier onto it.
        /// </summary>
        /// <remarks>
        ///     The identifier is read here rather than in a second pass because the table is already
        ///     cached on the open cache - <c>RSCache.GetReferenceTable</c> returns the parsed table
        ///     it holds - so the join costs a dictionary lookup per row and not a second read of
        ///     idx255.
        /// </remarks>
        /// <param name="cache">The open cache, for the identifier.</param>
        /// <param name="address">Where the payload came from.</param>
        /// <param name="payload">The decompressed script, whole.</param>
        /// <returns>The row.</returns>
        public override ClientScriptListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            //Read before Decode, which leaves the stream on the last byte by design so that a caller
            //measuring consumption sees the whole record consumed.
            int storedLength = payload.Length;

            var record = new ClientScriptDefinition { Id = address.DefinitionId };
            record.Decode(payload);

            RSArchiveEntry? entry = cache.GetReferenceTable(IndexId).GetArchiveEntry(address.GroupId);
            return new ClientScriptListing(address, record, entry?.GetIdentifier() ?? -1, storedLength);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(ClientScriptListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(ClientScriptListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }

        /// <summary>
        ///     Accepts an edited count only where the footer's 16-bit field can hold it.
        /// </summary>
        /// <remarks>
        ///     Refused rather than clamped, and refused here rather than left to
        ///     <see cref="ClientScriptDefinition.Encode"/>. Clamping writes a number the user did not
        ///     type into a field that decides how large a frame the client allocates; letting it
        ///     through to the encoder leaves the grid showing a value the file cannot store, next to
        ///     a status line saying the save failed. Keeping the old value is the only outcome that
        ///     leaves the row and the cache agreeing.
        /// </remarks>
        /// <param name="value">What the cell editor produced.</param>
        /// <param name="current">What the record holds now.</param>
        /// <returns>The value to store.</returns>
        private static int Storable(int value, int current) {
            return value >= 0 && value <= ClientScriptDefinition.MaxFooterCount ? value : current;
        }
    }
}
