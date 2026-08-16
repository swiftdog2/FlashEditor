using System;
using System.Collections.Generic;
using System.Globalization;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.Models;
using FlashEditor.Export;
using FlashEditor.IO;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.UI {
    /// <summary>
    ///     Says what an id points at, in a line, without going there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Following a link should be the last resort.</b> A user who has to open a tab, wait for
    ///     it to load and read a grid to find out that texture 41 is the one they already have
    ///     selected has been made to pay for a question that fits on one line. This answers it where
    ///     the cursor already is, and the back stack is what makes following worth doing when the
    ///     line is not enough.
    ///     </para>
    ///     <para>
    ///     <b>Every resolution comes from <see cref="CacheReferenceResolver"/>.</b> That type is the
    ///     export's, is the one place the measured joins are turned into addresses, and answers
    ///     existence from the reference table rather than by reading the target - which is the only
    ///     answer cheap enough to give from a mouse-move handler. Nothing here re-derives an address:
    ///     a second copy of that arithmetic would drift from the export's the first time an index's
    ///     split was corrected.
    ///     </para>
    /// </remarks>
    public sealed class CacheReferencePreview {
        /// <summary>
        ///     The join name every preview resolution is attributed to.
        /// </summary>
        /// <remarks>
        ///     <see cref="CacheReferenceResolver"/> records which relation produced a resolution so a
        ///     reader can check it against the measured list. A preview is not one of those relations
        ///     - it resolves whatever a column already declared - so it says so rather than borrowing
        ///     the name of a join it is not following.
        /// </remarks>
        private const string PreviewJoin = "link column preview";

        private readonly RSCache cache;
        private readonly CacheReferenceResolver resolver;

        /// <summary>Builds a preview over an open cache.</summary>
        /// <param name="cache">The open cache.</param>
        public CacheReferencePreview(RSCache cache) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            resolver = new CacheReferenceResolver(this.cache);
        }

        /// <summary>The resolver behind this preview, for a caller resolving a whole record's ids.</summary>
        /// <remarks>
        ///     Shared rather than handed out as a second instance, because the resolver caches a
        ///     reference table per index and the six small config groups it summarises. A second one
        ///     would read all of that again.
        /// </remarks>
        public CacheReferenceResolver Resolver => resolver;

        /// <summary>
        ///     What a cell's link or thumbnail points at, in one line.
        /// </summary>
        /// <param name="visual">The cell's own statement of what its number addresses.</param>
        /// <returns>The description, or null when the cell names nothing.</returns>
        public string? Describe(DefinitionCellVisual visual) {
            if (visual.Art != DefinitionCellArt.Link && visual.Art != DefinitionCellArt.Thumbnail)
                return null;

            return Describe(visual.IndexId, visual.TargetId, visual.GroupId);
        }

        /// <summary>
        ///     What an id points at, in one line.
        /// </summary>
        /// <remarks>
        ///     An id the reference table does not declare is reported as such rather than left to
        ///     read as an empty answer: a dangling id is a real thing to find in this cache, and
        ///     "nothing came back" would look identical to a preview that failed.
        /// </remarks>
        /// <param name="targetIndex">The index the id addresses.</param>
        /// <param name="targetId">The id.</param>
        /// <param name="configGroup">The group within index 2, or -1 for every other index.</param>
        /// <returns>The description, or null when the id is negative and so names nothing.</returns>
        public string? Describe(int targetIndex, int targetId, int configGroup = -1) {
            ExportedReference? reference =
                resolver.Resolve("preview", PreviewJoin, targetIndex, targetId, configGroup);

            if (reference == null)
                return null;

            return Describe(reference);
        }

        /// <summary>
        ///     One already-resolved reference, in a line.
        /// </summary>
        /// <param name="reference">The resolution.</param>
        /// <returns>The description.</returns>
        public static string Describe(ExportedReference reference) {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));

            string where = reference.TargetIndex == RSConstants.CONFIG
                ? ConfigFamily.For(reference.TargetGroup).RowNoun + " " +
                  reference.Id.ToString(CultureInfo.InvariantCulture) + " in config group " +
                  reference.TargetGroup.ToString(CultureInfo.InvariantCulture) + ", " +
                  ConfigFamily.For(reference.TargetGroup).Name
                : RSConstants.GetIndexName(reference.TargetIndex) + " " +
                  reference.Id.ToString(CultureInfo.InvariantCulture) + " (index " +
                  reference.TargetIndex.ToString(CultureInfo.InvariantCulture) + ")";

            if (!reference.Exists)
                return where + " - the reference table does not declare it";

            return string.IsNullOrEmpty(reference.Detail) ? where : where + " - " + reference.Detail;
        }

        /// <summary>
        ///     Every id one decoded record names, resolved.
        /// </summary>
        /// <remarks>
        ///     Straight through <see cref="CacheExportJoins.Extract"/>, which is the project's single
        ///     statement of which relations are measured and resolving in this cache. Adding an arm
        ///     here rather than there would put a join in the editor that the export does not make and
        ///     nothing checks, which is exactly the shape the world map icon join failed in.
        /// </remarks>
        /// <param name="row">The decoded record, in whatever form the row's own tab produces.</param>
        /// <returns>The resolutions, possibly none.</returns>
        public IReadOnlyList<ExportedReference> ReferencesOf(object row) {
            object? subject = row == null ? null : AsExportRecord(row);
            if (subject == null)
                return Array.Empty<ExportedReference>();

            var resolved = new List<ExportedReference>();
            try {
                foreach (ExportedReference reference in CacheExportJoins.Extract(subject, resolver))
                    resolved.Add(reference);
            } catch (Exception failure) {
                //A record that will not resolve costs its own references and nothing else. This runs
                //from a selection change, and an exception out of one would take the tab with it.
                Debug("Could not resolve the references of a " + subject.GetType().Name + ": " +
                    failure.Message, LOG_DETAIL.ADVANCED);
            }

            return resolved;
        }

        /// <summary>
        ///     The grid's row as the record shape <see cref="CacheExportJoins"/> knows.
        /// </summary>
        /// <remarks>
        ///     Seven of the ten row types the export dispatches on are the same objects the grids
        ///     hold, so they pass straight through. Two are not, and both are types the export builds
        ///     rather than decodes:
        ///     <list type="bullet">
        ///     <item>
        ///     A model row carries no bytes at all. <c>ModelListDescriptor</c> declares
        ///     <c>ReadsPayload</c> false because index 7 is 63,607 groups of one file and a grid of
        ///     ids needs none of them - so the footer that names the emitters, effectors and
        ///     billboards is read here, for the one model asked about, and nowhere else.
        ///     </item>
        ///     <item>
        ///     A MIDI patch row holds the decoded patch but not the per-key census the export's
        ///     record walks, which is the thing that says whether a key's sample is an index 4 or an
        ///     index 14 file.
        ///     </item>
        ///     </list>
        /// </remarks>
        /// <param name="row">The grid's row.</param>
        /// <returns>The record to extract from, or null when this row type names nothing.</returns>
        private object? AsExportRecord(object row) {
            switch (row) {
                case ModelListing model:
                    try {
                        JagStream payload = cache.ReadFile(RSConstants.MODELS_INDEX,
                            model.Address.GroupId, model.Address.FileId);
                        return new ModelReferenceRecord(model.Address.GroupId, model.Address.FileId,
                            ModelCodec.Decode(payload, model.Address.GroupId));
                    } catch (Exception failure) {
                        Debug("Could not read model " + model.ModelId + " to list its references: " +
                            failure.Message, LOG_DETAIL.ADVANCED);
                        return null;
                    }

                case MidiPatchListing patch:
                    return new MidiPatchRecord(patch.Address.GroupId, patch.Address.FileId, patch.Patch);

                default:
                    return row;
            }
        }
    }
}
