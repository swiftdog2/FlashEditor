using System;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.VarBits {
    /// <summary>
    ///     One varbit from index 22 as a list row.
    /// </summary>
    /// <remarks>
    ///     A row is a file, including the files that hold nothing but a terminator. Those are not
    ///     absent varbits and must not be hidden: they are declared slots whose record was never
    ///     written, they re-encode to the one byte they were read from, and
    ///     <see cref="IsStored"/> is the only thing that tells them apart from a record whose fields
    ///     happen to be zero.
    /// </remarks>
    public sealed class VarBitListing {
        /// <summary>Binds one decoded varbit to where it came from.</summary>
        /// <param name="address">The group and file, and the varbit id they carry.</param>
        /// <param name="record">The decoded varbit.</param>
        public VarBitListing(DefinitionAddress address, VarBitDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>Where the varbit lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public VarBitDefinition Record { get; }

        /// <summary>The varbit id, which is <c>(group &lt;&lt; 10) | file</c>.</summary>
        public int VarBitId => Record.Id;

        /// <summary>The bank of 1024 ids this varbit sits in.</summary>
        public int GroupId => Address.GroupId;

        /// <summary>The file within that bank.</summary>
        public int FileId => Address.FileId;

        /// <summary>Whether the file carried a record at all.</summary>
        public bool IsStored => Record.IsStored;

        /// <summary>The player variable the bits are taken from.</summary>
        public int VarpId {
            get => Record.VarpId;
            set => Record.VarpId = value;
        }

        /// <summary>The least significant bit of the range.</summary>
        public int FromBit {
            get => Record.FromBit;
            set => Record.FromBit = value;
        }

        /// <summary>The most significant bit of the range.</summary>
        public int ToBit {
            get => Record.ToBit;
            set => Record.ToBit = value;
        }

        /// <summary>The range as the client would read it, or nothing when the file stored none.</summary>
        public string BitRange => IsStored ? FromBit + ".." + ToBit : string.Empty;

        /// <summary>How many bits wide the range is.</summary>
        public object? Width => IsStored ? (object?) Record.BitWidth : null;

        /// <summary>The mask applied after shifting the varp down, in hex.</summary>
        public string Mask => IsStored ? "0x" + Record.Mask.ToString("X") : string.Empty;

        /// <summary>
        ///     Whether the client could load this record without indexing past its mask table.
        /// </summary>
        /// <remarks>
        ///     A column rather than a validation rule, because the editor has no place to refuse an
        ///     edit from - and a range the client throws on is worth seeing before the cache is
        ///     saved rather than after it fails to load. Every shipped record fits.
        /// </remarks>
        public string Fits => !IsStored ? string.Empty : Record.FitsTheClientMaskTable ? "" : "out of range";
    }

    /// <summary>
    ///     Index 22 as a definition list: one row per varbit, decoded and re-encodable.
    /// </summary>
    /// <remarks>
    ///     The record format has exactly one opcode - <c>VarBit.method3945</c> reads a u16 varp and
    ///     two bit positions (VarBit.java:47-80) - and the 639 data carries exactly that one, so this
    ///     is one of the few indexes where matching the client is unambiguously right.
    ///     <para>
    ///     <b>Editable in all three fields</b>, and the absent-versus-default rule is what makes that
    ///     safe: <see cref="VarBitDefinition.Encode"/> keeps a bare terminator as a bare terminator
    ///     unless a field actually moved, so browsing and saving does not rewrite the quarter of the
    ///     index that stores nothing.
    ///     </para>
    ///     <para>
    ///     The descriptor also builds the varp-to-varbits index the detail pane reads. It is here
    ///     rather than in the panel because the panel only ever sees the selected row, and rebuilding
    ///     the reverse index would mean decoding the whole of index 22 a second time.
    ///     </para>
    /// </remarks>
    public sealed class VarBitListDescriptor : DefinitionListDescriptor<VarBitListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /* Written from the load worker and read from the UI thread, so both sides take the lock.
           Cleared by Enumerate, which the panel calls exactly once at the start of each load. */
        private readonly object varpLock = new object();
        private readonly Dictionary<int, List<VarBitListing>> byVarp = new Dictionary<int, List<VarBitListing>>();

        /// <summary>Lists every varbit the index declares.</summary>
        public VarBitListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<VarBitListing>("Varbit", row => row.VarBitId, 80),
                DefinitionColumn.ReadOnly<VarBitListing>("Group", row => row.GroupId, 70),
                DefinitionColumn.ReadOnly<VarBitListing>("File", row => row.FileId, 60),
                DefinitionColumn.Number<VarBitListing>("Varp", row => row.IsStored ? (object?) row.VarpId : null,
                    (row, value) => row.VarpId = value, 80),
                DefinitionColumn.Number<VarBitListing>("From bit", row => row.IsStored ? (object?) row.FromBit : null,
                    (row, value) => row.FromBit = value, 90),
                DefinitionColumn.Number<VarBitListing>("To bit", row => row.IsStored ? (object?) row.ToBit : null,
                    (row, value) => row.ToBit = value, 80),
                DefinitionColumn.ReadOnly<VarBitListing>("Width", row => row.Width, 70),
                DefinitionColumn.ReadOnly<VarBitListing>("Mask", row => row.Mask, 110),
                DefinitionColumn.ReadOnly<VarBitListing>("Stored", row => row.IsStored ? "yes" : "no", 80),
                DefinitionColumn.ReadOnly<VarBitListing>("Client", row => row.Fits, 110)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.SCRIPT_CONFIGS;

        /// <inheritdoc/>
        public override string RowNoun => "varbit";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <summary>
        ///     Every row the index holds, and the point at which the varp index is emptied.
        /// </summary>
        /// <remarks>
        ///     Cleared here rather than in <c>Decode</c> because this is called once per load while
        ///     <c>Decode</c> is called once per row.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The addresses to load.</returns>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            lock (varpLock)
                byVarp.Clear();

            return base.Enumerate(cache);
        }

        /// <inheritdoc/>
        public override VarBitListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new VarBitDefinition { Id = address.DefinitionId };
            record.Decode(payload);

            var listing = new VarBitListing(address, record);

            //Only stored records are indexed. A bare terminator decodes to varp 0, and filing those
            //under varp 0 would bury its real varbits under a thousand slots that name nothing.
            if (record.IsStored) {
                lock (varpLock) {
                    if (!byVarp.TryGetValue(record.VarpId, out List<VarBitListing>? siblings)) {
                        siblings = new List<VarBitListing>();
                        byVarp[record.VarpId] = siblings;
                    }
                    siblings.Add(listing);
                }
            }

            return listing;
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(VarBitListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(VarBitListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }

        /// <summary>
        ///     Every varbit that carves its bits out of one player variable, in id order.
        /// </summary>
        /// <remarks>
        ///     This is the level a varbit only has in relation to others: on its own a bit range is
        ///     three numbers, and against its siblings it is one field of a packed variable whose
        ///     other fields are visible. A varp that is still loading yields what has been decoded so
        ///     far rather than blocking the UI thread behind the sweep.
        /// </remarks>
        /// <param name="varpId">The player variable.</param>
        /// <returns>The varbits pointing at it, or an empty list.</returns>
        public IReadOnlyList<VarBitListing> SiblingsOf(int varpId) {
            lock (varpLock) {
                if (!byVarp.TryGetValue(varpId, out List<VarBitListing>? siblings))
                    return Array.Empty<VarBitListing>();

                //Copied under the lock: the worker may still be adding to this list, and the caller
                //is about to iterate it on the UI thread.
                var snapshot = new List<VarBitListing>(siblings);
                snapshot.Sort((left, right) => left.VarBitId.CompareTo(right.VarBitId));
                return snapshot;
            }
        }
    }
}
