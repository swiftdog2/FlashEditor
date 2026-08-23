using System;
using System.Collections.Generic;
using FlashEditor.Cache;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     The rules index 3 imposes on a group's contents, wherever that group comes from.
    /// </summary>
    /// <remarks>
    ///     Shared by <see cref="InterfaceStructureWriter"/>, which edits a group's files, and
    ///     <see cref="InterfaceGroupWriter"/>, which makes the group. One statement of the rule and
    ///     one message rather than a second copy that drifts from the first.
    ///     <para>
    ///     None of this belongs in <c>RSCache</c>. Sparse groups are legal in this format and
    ///     normal on other indexes, so the archive layer deliberately does not impose density on
    ///     every write; index 3 is the caller that knows.
    ///     </para>
    /// </remarks>
    internal static class InterfaceGroupRules {
        /// <summary>
        ///     Refuses a file set that is not numbered 0 to n-1.
        /// </summary>
        /// <remarks>
        ///     The client derives a group's file count as <c>maxFileId + 1</c> and drops the
        ///     explicit id list whenever that agrees with the declared count
        ///     (<c>VersionTable.java:183,185</c>), so a hole is read as a component that is not
        ///     there and shifts every component after it onto a different id.
        ///     <para>
        ///     A check rather than a fix. Closing a hole here would perform a renumbering the user
        ///     was never shown and never warned about, which is what the planner's warnings exist to
        ///     prevent.
        ///     </para>
        /// </remarks>
        /// <param name="groupId">The interface, for the message.</param>
        /// <param name="files">The proposed contents, ascending.</param>
        /// <exception cref="InvalidOperationException">The set is empty or numbered with a hole.</exception>
        internal static void RequireDenseNumbering(int groupId, IReadOnlyList<RSGroupFile> files) {
            if (files.Count == 0)
                throw new InvalidOperationException(
                    "Interface " + groupId + " would be left with no components. A group with no" +
                    " payload cannot be stored, so deleting the last component means deleting the" +
                    " interface, which is a different operation.");

            for (int i = 0; i < files.Count; i++) {
                if (files[i].FileId == i)
                    continue;

                throw new InvalidOperationException(
                    "Interface " + groupId + " would be numbered with a hole: position " + i +
                    " holds component " + files[i].FileId + ". The client reads a group's file count" +
                    " as the highest id plus one, so a hole shifts every component after it onto a" +
                    " different id.");
            }
        }
    }
}
