using System;
using BrightIdeasSoftware;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     Adds a read-only column to a grid that is not driven by a descriptor.
    /// </summary>
    /// <remarks>
    ///     <b>One implementation of the null-row rule, because ten copies of it had none.</b> Every
    ///     detail pane and master list outside <see cref="DefinitionListPanel"/> had grown its own
    ///     private <c>AddColumn</c> plus a hard-casting row helper, and not one of them guarded the
    ///     null model <see cref="ObjectListView"/> hands an aspect getter. The rule was written down
    ///     in the repository's UI conventions and implemented once, in
    ///     <see cref="DefinitionColumn"/>, and then re-implemented ten times without it.
    ///     <para>
    ///     <b>It is not a theoretical hazard.</b> Opening a cache, editing an interface, then
    ///     closing without saving threw <c>NullReferenceException</c> out of the interfaces master
    ///     list: unbinding calls <c>ClearObjects</c>, the grid evaluates aspects for the rows it is
    ///     recycling, and <c>(InterfaceListing) null</c> is a perfectly legal cast whose
    ///     <c>.GroupId</c> is not.
    ///     </para>
    ///     <para>
    ///     <b>A row of the wrong type still throws, and the message says which two types.</b> That
    ///     can only mean a grid was wired to a row type it does not produce, and blanking those
    ///     cells would hide it permanently - the same reasoning, and the same behaviour, as
    ///     <c>DefinitionColumn.Cast</c>.
    ///     </para>
    /// </remarks>
    public static class DetailGrid {
        /// <summary>
        ///     Adds a column that reads its value off a typed row.
        /// </summary>
        /// <param name="list">The grid to add to.</param>
        /// <param name="heading">The column heading.</param>
        /// <param name="width">The column width, in the grid's own pinned font.</param>
        /// <param name="read">
        ///     Reads the displayed value off a row. <b>Never called with null</b>, so the caller's
        ///     own cast helper may assume a row is there.
        /// </param>
        public static void AddColumn(ObjectListView list, string heading, int width,
            Func<object, object?> read) {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            if (read == null)
                throw new ArgumentNullException(nameof(read));

            var column = new OLVColumn(heading, null) {
                Width = width,
                Groupable = false,
                IsEditable = false,

                /* The guard, and the only reason this type exists. A null row is a legitimate
                   state: the grid evaluates aspects for rows it is recycling during a scroll, for
                   cells it measures before a model is attached, and while a bind tears the list
                   down. An empty cell is the right answer; the caller's cast never sees it.

                   A row of the WRONG type still reaches the caller and still throws there, which is
                   correct - that can only mean a grid was wired to a row type it does not produce,
                   and blanking those cells would hide it permanently. */
                AspectGetter = row => row == null ? null : read(row)
            };

            list.AllColumns.Add(column);
            list.Columns.Add(column);
        }
    }
}
