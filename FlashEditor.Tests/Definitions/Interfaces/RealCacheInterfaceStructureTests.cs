using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     What index 3's numbering actually is, measured rather than quoted.
    /// </summary>
    /// <remarks>
    ///     <b>Two documents in this tree disagreed about it, and a structural edit cannot be written
    ///     until one of them is settled.</b> <c>InterfaceComponentEdits</c> states that file ids are
    ///     dense, 0 to n-1, in every group and that the client depends on it;
    ///     <c>InterfaceEditorPanel.InterfaceListing.IdRange</c> states the opposite in a comment,
    ///     that "index 3's groups are sparse, the count and the highest id disagree". The renumbering
    ///     every operation in <c>InterfaceComponentEdits</c> performs exists only if the first is
    ///     true, so this measures it against the loaded cache and prints the result.
    ///     <para>
    ///     Read from the reference table, never decoded, so it costs nothing and belongs to neither
    ///     cache: it asserts the shape of the declared id list, which is a property of the format,
    ///     and prints the group and file totals rather than asserting them.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheInterfaceStructureTests : IClassFixture<RealCacheFixture> {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheInterfaceStructureTests(RealCacheFixture fixture, ITestOutputHelper output) {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every interface the table declares numbers its components 0 to n-1 with no hole.
        /// </summary>
        /// <remarks>
        ///     This is what makes renumbering compulsory rather than tidy. The client derives a
        ///     group's file count as <c>maxFileId + 1</c> and throws the explicit id list away
        ///     whenever that agrees with the declared count (<c>VersionTable.java:183,185</c>), so a
        ///     group left with a hole is read with a file count that does not match its contents and
        ///     every component after the hole is addressed as a different one.
        /// </remarks>
        [RealCacheFact]
        public void EveryInterface_NumbersItsComponentsDenselyFromZero() {
            RSReferenceTable table = _fixture.Table(RSConstants.INTERFACE_DEFINITIONS_INDEX);

            var sparse = new List<string>();
            int groups = 0;
            int files = 0;

            foreach (int groupId in _fixture.OpenCache().EnumerateGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX)) {
                RSArchiveEntry entry = table.GetArchiveEntry(groupId);
                if (entry == null)
                    continue;

                int[] ids = entry.GetValidFileIds();
                groups++;
                files += ids.Length;

                for (int i = 0; i < ids.Length; i++) {
                    if (ids[i] == i)
                        continue;

                    sparse.Add("group " + groupId + " declares " + ids.Length + " files, highest id " +
                        ids[ids.Length - 1] + ", first hole at position " + i + " (id " + ids[i] + ")");
                    break;
                }
            }

            _output.WriteLine("index 3: " + groups + " groups, " + files + " files declared");
            _output.WriteLine(sparse.Count == 0
                ? "every group is dense 0..n-1"
                : sparse.Count + " groups are not dense:");

            foreach (string line in sparse.Take(20))
                _output.WriteLine("  " + line);

            Assert.Empty(sparse);
        }
    }
}
