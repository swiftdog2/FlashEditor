using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.IO;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     Covers <see cref="InterfaceGroupWriter"/>, which makes and copies whole interfaces.
    /// </summary>
    /// <remarks>
    ///     Against a synthetic index 3 rather than the real cache, because what is being asserted is
    ///     the rules the writer applies rather than anything about the shipped data - and a
    ///     hand-built index has an append slot that is known rather than measured.
    ///     <para>
    ///     The seeded interface is stored uncompressed so an untouched neighbour can be compared on
    ///     its stored bytes. A GZip re-encode is never byte-identical, so comparing decompressed
    ///     payloads would pass over a neighbour that had been rewritten.
    ///     </para>
    /// </remarks>
    public class InterfaceGroupWriterTests : IDisposable {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in a const context

        private const int Index = RSConstants.INTERFACE_DEFINITIONS_INDEX;

        /// <summary>The interface the fixture seeds, so the append slot is 1.</summary>
        private const int Existing = 0;

        /// <summary>The name the seeded interface carries, for the collision case.</summary>
        private const string ExistingName = "inventory";

        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public InterfaceGroupWriterTests() {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-ifgroup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() {
            foreach (RSFileStore store in _stores)
                store.Dispose();
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        // ===================================================================
        //  Seeding
        // ===================================================================

        /// <summary>Two components, so the seeded interface is a shape a copy can be checked against.</summary>
        private static InterfaceComponentDefinition[] SeededComponents() {
            InterfaceComponentDefinition root = InterfaceComponentEdits.NewRootComponent(Existing,
                InterfaceComponentEdits.RootComponentType.Container, 512, 334);
            InterfaceComponentDefinition child = InterfaceComponentEdits.NewComponent(Existing, 1, 0);

            /* A hook argument naming a component of this very interface, folded the way the client
               folds one. It is what gives a copy something real to warn about: the operand is a
               plain integer and survives a copy unchanged, so it goes on addressing the original. */
            child.Hooks[0] = new[] {
                InterfaceScriptOperand.OfInteger((Existing << 16) | 1)
            };

            return new[] { root, child };
        }

        private static byte[] SeededPayload() {
            var archive = new RSArchive();
            foreach (InterfaceComponentDefinition component in SeededComponents())
                archive.PutFile(component.FileId, component.Encode());
            return archive.Encode().ToArray();
        }

        private static JagStream StoredContainer() {
            return new RSContainer(Index, Existing, RSConstants.NO_COMPRESSION,
                new JagStream(SeededPayload()), 1337).Encode();
        }

        private RSCache CreateCache() {
            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);

            //Padded to the index's own slot, because RSFileStore.Write needs contiguous archive ids
            //and the meta index addresses a table by the index it describes.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + Index), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + RSConstants.META_INDEX),
                new byte[Index * 6]);

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            store.Write(Index, Existing, StoredContainer());
            store.Write(RSConstants.META_INDEX, Index, EncodeReferenceTable());

            return new RSCache(store);
        }

        private static JagStream EncodeReferenceTable() {
            var table = new RSReferenceTable {
                format = 6, version = 1131, flags = RSReferenceTable.FLAG_IDENTIFIERS
            };
            byte[] stored = StoredContainer().ToArray();

            var entry = new RSArchiveEntry(Existing);
            entry.SetVersion(1);
            entry.SetIdentifier(NameHasher.GetNameHash(ExistingName));
            entry.SetCrc(unchecked((int) CRC32Helper.ComputeCrc32(stored.AsSpan(0, stored.Length - 2))));

            var fileEntries = new SortedDictionary<int, RSFileEntry>();
            var ids = new List<int>();
            foreach (InterfaceComponentDefinition component in SeededComponents()) {
                var child = new RSFileEntry(component.FileId);
                child.SetIdentifier(NameHasher.GetNameHash("com_" + component.FileId));
                fileEntries[component.FileId] = child;
                ids.Add(component.FileId);
            }

            entry.SetValidFileIds(ids.ToArray());
            entry.SetFileEntries(fileEntries);
            table.PutArchiveEntry(Existing, entry);

            return new RSContainer(RSConstants.META_INDEX, Index, RSConstants.GZIP_COMPRESSION,
                ReferenceTableCodec.Encode(table), 1337).Encode();
        }

        private RSCache SaveAndReopen(RSCache cache) {
            string outDir = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);

            var reopened = new RSFileStore(outDir);
            _stores.Add(reopened);
            return new RSCache(reopened);
        }

        private static InterfaceComponentDefinition Decode(RSCache cache, int groupId, int fileId) {
            JagStream payload = cache.ReadGroup(Index, groupId)[fileId];
            payload.Seek0();
            return new InterfaceComponentDefinition(groupId, fileId).Decode(payload);
        }

        // ===================================================================
        //  Creating
        // ===================================================================

        [Fact]
        public void Create_TakesTheAppendSlotAndDeclaresItsComponent() {
            RSCache cache = CreateCache();

            int created = InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                InterfaceComponentEdits.NewRootComponent(1,
                    InterfaceComponentEdits.RootComponentType.Container, 512, 334)
            });

            Assert.Equal(1, created);

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(new[] { 0 },
                reopened.GetReferenceTable(Index).GetArchiveEntry(created).GetValidFileIds());
        }

        /// <summary>
        ///     A container root has to survive the encode arms only it reaches.
        /// </summary>
        /// <remarks>
        ///     The scroll extents and the layer flag byte are written for type 0 alone, and the
        ///     layer flag only while the version reads negative. Every other component the editor
        ///     creates is a type 3, so nothing else in the suite exercises them on a written record.
        /// </remarks>
        [Fact]
        public void Create_WithAContainerRoot_RoundTripsTheLayerOnlyFields() {
            RSCache cache = CreateCache();

            int created = InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                InterfaceComponentEdits.NewRootComponent(1,
                    InterfaceComponentEdits.RootComponentType.Container, 512, 334)
            });

            InterfaceComponentDefinition root = Decode(SaveAndReopen(cache), created, 0);

            Assert.Equal(0, root.ComponentType);
            Assert.Equal(512, root.BaseWidth);
            Assert.Equal(334, root.BaseHeight);
            Assert.Equal(InterfaceComponentDefinition.NoParent, root.RawParentId);
            Assert.Equal(-1, root.Version);
            Assert.Equal(0, root.ScrollMaxHorizontal);
            Assert.Equal(0, root.ScrollMaxVertical);
            Assert.Equal(0, root.LayerFlagByte);
        }

        [Fact]
        public void Create_WithARectangleRoot_RoundTripsItsColourAndFill() {
            RSCache cache = CreateCache();

            int created = InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                InterfaceComponentEdits.NewRootComponent(1,
                    InterfaceComponentEdits.RootComponentType.Rectangle, 64, 32)
            });

            InterfaceComponentDefinition root = Decode(SaveAndReopen(cache), created, 0);

            Assert.Equal(3, root.ComponentType);
            Assert.Equal(0x808080, root.Colour);
            Assert.True(root.RectangleFilled);
        }

        /// <summary>
        ///     The interface has to show the name it was created with.
        /// </summary>
        /// <remarks>
        ///     The shipped name table is keyed by group id and cannot know about a group that did
        ///     not exist when it was written, so without the created-name memory the name would be
        ///     in the cache and nowhere on screen.
        /// </remarks>
        [Fact]
        public void Create_LeavesTheNameResolvableAndTheHashStored() {
            RSCache cache = CreateCache();
            int created = InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                InterfaceComponentEdits.NewRootComponent(1,
                    InterfaceComponentEdits.RootComponentType.Container, 512, 334)
            });

            RSCache reopened = SaveAndReopen(cache);
            int stored = reopened.GetReferenceTable(Index).GetArchiveEntry(created).GetIdentifier();

            Assert.Equal(NameHasher.GetNameHash("my_screen"), stored);
            Assert.Equal("my_screen", InterfaceNames.GroupName(created, stored));
            Assert.Equal(created, reopened.GetReferenceTable(Index).Names.GroupId("my_screen"));
        }

        /// <summary>
        ///     A remembered name is a candidate, not an assertion.
        /// </summary>
        /// <remarks>
        ///     It is still refused when it does not hash to the identifier the table holds, which is
        ///     what keeps the memory safe across a change of cache: a name remembered for group 1 of
        ///     one cache says nothing about group 1 of another.
        /// </remarks>
        [Fact]
        public void ACreatedName_IsStillRefusedWhenItDoesNotHashToTheStoredIdentifier() {
            RSCache cache = CreateCache();
            int created = InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                InterfaceComponentEdits.NewRootComponent(1,
                    InterfaceComponentEdits.RootComponentType.Container, 512, 334)
            });

            Assert.Null(InterfaceNames.GroupName(created, NameHasher.GetNameHash("something_else")));
        }

        /// <summary>
        ///     A created component carries the generated name the cache's own convention gives it.
        /// </summary>
        /// <remarks>
        ///     9,219 of index 3's components are named com_&lt;fileId&gt; and in every one of them
        ///     the number is the component's own file id, so this is the name the format would give
        ///     it rather than one invented here.
        /// </remarks>
        [Fact]
        public void Create_NamesItsComponentsByTheGeneratedConvention() {
            RSCache cache = CreateCache();
            int created = InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                InterfaceComponentEdits.NewRootComponent(1,
                    InterfaceComponentEdits.RootComponentType.Container, 512, 334)
            });

            RSCache reopened = SaveAndReopen(cache);
            int identifier = reopened.GetReferenceTable(Index)
                .GetArchiveEntry(created).GetFileEntry(0).GetIdentifier();

            Assert.Equal("com_0", InterfaceNames.ComponentName(created, 0, identifier));
        }

        [Fact]
        public void Create_LeavesTheExistingInterfaceStoredBytesAlone() {
            RSCache cache = CreateCache();
            byte[] before = cache.LoadContainer(Index, Existing).ToArray();

            InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                InterfaceComponentEdits.NewRootComponent(1,
                    InterfaceComponentEdits.RootComponentType.Container, 512, 334)
            });

            Assert.Equal(before, SaveAndReopen(cache).LoadContainer(Index, Existing).ToArray());
        }

        // ===================================================================
        //  Refusals
        // ===================================================================

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Create_WithoutAName_IsRefused(string name) {
            RSCache cache = CreateCache();

            var thrown = Assert.Throws<ArgumentException>(() => InterfaceGroupWriter.Create(cache, name, new[] {
                InterfaceComponentEdits.NewRootComponent(1,
                    InterfaceComponentEdits.RootComponentType.Container, 512, 334)
            }));

            Assert.Contains("has to be named", thrown.Message);
        }

        [Fact]
        public void Create_WithANameAnotherInterfaceCarries_IsRefused() {
            RSCache cache = CreateCache();

            var thrown = Assert.Throws<InvalidOperationException>(
                () => InterfaceGroupWriter.Create(cache, ExistingName, new[] {
                    InterfaceComponentEdits.NewRootComponent(1,
                        InterfaceComponentEdits.RootComponentType.Container, 512, 334)
                }));

            Assert.Contains("already called", thrown.Message);
        }

        [Fact]
        public void Create_WithNoComponents_IsRefused() {
            RSCache cache = CreateCache();

            Assert.Throws<InvalidOperationException>(() => InterfaceGroupWriter.Create(cache, "my_screen",
                Array.Empty<InterfaceComponentDefinition>()));
        }

        /// <summary>
        ///     Components numbered with a hole are refused rather than closed up.
        /// </summary>
        /// <remarks>
        ///     The client reads a group's file count as the highest id plus one, so a hole is read
        ///     as a component that is not there and shifts every component after it.
        /// </remarks>
        [Fact]
        public void Create_WithAHoleInTheNumbering_IsRefused() {
            RSCache cache = CreateCache();

            var thrown = Assert.Throws<InvalidOperationException>(
                () => InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                    InterfaceComponentEdits.NewRootComponent(1,
                        InterfaceComponentEdits.RootComponentType.Container, 512, 334),
                    InterfaceComponentEdits.NewComponent(1, 2, 0)
                }));

            Assert.Contains("hole", thrown.Message);
        }

        /// <summary>
        ///     A component built against another interface is refused.
        /// </summary>
        /// <remarks>
        ///     A parent reference is stored as a bare sibling index and folded against the group id
        ///     on read, so a component built for a different group encodes a parent that resolves
        ///     into that group instead of this one - which decodes cleanly and points somewhere
        ///     else.
        /// </remarks>
        [Fact]
        public void Create_WithAComponentBuiltForAnotherInterface_IsRefused() {
            RSCache cache = CreateCache();

            var thrown = Assert.Throws<InvalidOperationException>(
                () => InterfaceGroupWriter.Create(cache, "my_screen", new[] {
                    InterfaceComponentEdits.NewRootComponent(97,
                        InterfaceComponentEdits.RootComponentType.Container, 512, 334)
                }));

            Assert.Contains("built for interface 97", thrown.Message);
        }

        // ===================================================================
        //  Duplicating
        // ===================================================================

        /// <summary>
        ///     A copy has to hold the same components, byte for byte.
        /// </summary>
        /// <remarks>
        ///     Compared on the decompressed payload rather than the stored container, because the
        ///     copy is stored GZip and the source uncompressed - a container comparison would fail
        ///     for a copy that was perfectly correct.
        /// </remarks>
        [Fact]
        public void Duplicate_ReproducesEveryComponentByteForByte() {
            RSCache cache = CreateCache();

            InterfaceGroupWriter.DuplicateResult result =
                InterfaceGroupWriter.Duplicate(cache, Existing, "my_copy");

            RSCache reopened = SaveAndReopen(cache);
            IReadOnlyDictionary<int, JagStream> source = reopened.ReadGroup(Index, Existing);
            IReadOnlyDictionary<int, JagStream> copy = reopened.ReadGroup(Index, result.GroupId);

            Assert.Equal(source.Keys.OrderBy(id => id), copy.Keys.OrderBy(id => id));

            foreach (int fileId in source.Keys) {
                source[fileId].Seek0();
                copy[fileId].Seek0();
                Assert.Equal(source[fileId].ToArray(), copy[fileId].ToArray());
            }
        }

        /// <summary>
        ///     Every per-file name travels with the copy.
        /// </summary>
        /// <remarks>
        ///     Correct here specifically because a copy preserves the numbering, so a generated
        ///     com_&lt;fileId&gt; hash is still true of the file it lands on. A copy that renumbered
        ///     would have to rehash them.
        /// </remarks>
        [Fact]
        public void Duplicate_CarriesEveryComponentName() {
            RSCache cache = CreateCache();

            InterfaceGroupWriter.DuplicateResult result =
                InterfaceGroupWriter.Duplicate(cache, Existing, "my_copy");

            RSCache reopened = SaveAndReopen(cache);
            RSArchiveEntry source = reopened.GetReferenceTable(Index).GetArchiveEntry(Existing);
            RSArchiveEntry copy = reopened.GetReferenceTable(Index).GetArchiveEntry(result.GroupId);

            foreach (int fileId in source.GetValidFileIds()) {
                Assert.Equal(source.GetFileEntry(fileId).GetIdentifier(),
                    copy.GetFileEntry(fileId).GetIdentifier());
                Assert.Equal("com_" + fileId,
                    InterfaceNames.ComponentName(result.GroupId, fileId,
                        copy.GetFileEntry(fileId).GetIdentifier()));
            }
        }

        /// <summary>
        ///     The copy takes the name it was given, never the one it was copied from.
        /// </summary>
        /// <remarks>
        ///     Two groups sharing a name hash leave one of them unreachable by name, and which one
        ///     depends on the order the client hash table happens to reach them.
        /// </remarks>
        [Fact]
        public void Duplicate_TakesItsOwnNameAndNotTheSourceName() {
            RSCache cache = CreateCache();

            InterfaceGroupWriter.DuplicateResult result =
                InterfaceGroupWriter.Duplicate(cache, Existing, "my_copy");

            RSCache reopened = SaveAndReopen(cache);
            RSReferenceTable table = reopened.GetReferenceTable(Index);

            Assert.Equal(NameHasher.GetNameHash("my_copy"),
                table.GetArchiveEntry(result.GroupId).GetIdentifier());
            Assert.Equal(Existing, table.Names.GroupId(ExistingName));
            Assert.Equal(result.GroupId, table.Names.GroupId("my_copy"));
        }

        [Fact]
        public void Duplicate_LeavesTheSourceStoredBytesAlone() {
            RSCache cache = CreateCache();
            byte[] before = cache.LoadContainer(Index, Existing).ToArray();

            InterfaceGroupWriter.Duplicate(cache, Existing, "my_copy");

            Assert.Equal(before, SaveAndReopen(cache).LoadContainer(Index, Existing).ToArray());
        }

        /// <summary>
        ///     A copy is never wired to anything and has to say so.
        /// </summary>
        /// <remarks>
        ///     The warning is unconditional because the absence of references to the copy is not
        ///     something that can be detected - CS2 scripts in index 12 address components as
        ///     packed integers and nothing here scans them. A caller that showed no warning would be
        ///     stating the copy works, which is the one thing it certainly does not.
        /// </remarks>
        [Fact]
        public void Duplicate_AlwaysWarnsThatNothingDrivesTheCopy() {
            RSCache cache = CreateCache();

            InterfaceGroupWriter.DuplicateResult result =
                InterfaceGroupWriter.Duplicate(cache, Existing, "my_copy");

            Assert.NotEmpty(result.Warnings);
            Assert.Contains(result.Warnings, warning => warning.Contains("CS2 scripts in index 12"));
        }

        /// <summary>
        ///     A hook that addresses the source is reported, and left alone.
        /// </summary>
        /// <remarks>
        ///     Rewriting the high half of every operand that happens to equal the source group id is
        ///     the heuristic the renumber planner deliberately only warns about: such an operand may
        ///     be an ordinary constant, and rewriting one corrupts it with nothing to notice.
        /// </remarks>
        [Fact]
        public void Duplicate_ReportsHooksStillPointingAtTheSourceRatherThanRewritingThem() {
            RSCache cache = CreateCache();

            InterfaceGroupWriter.DuplicateResult result =
                InterfaceGroupWriter.Duplicate(cache, Existing, "my_copy");

            Assert.Contains(result.Warnings,
                warning => warning.Contains("hook arguments") && warning.Contains("interface " + Existing));

            //And the operand really is untouched in the copy.
            InterfaceComponentDefinition copied = Decode(SaveAndReopen(cache), result.GroupId, 1);
            Assert.Equal((Existing << 16) | 1, copied.Hooks[0][0].Integer);
        }

        [Fact]
        public void Duplicate_WithANameAnotherInterfaceCarries_IsRefused() {
            RSCache cache = CreateCache();

            Assert.Throws<InvalidOperationException>(
                () => InterfaceGroupWriter.Duplicate(cache, Existing, ExistingName));
        }

        [Fact]
        public void Duplicate_OfAnInterfaceTheTableDoesNotDeclare_IsRefused() {
            RSCache cache = CreateCache();

            var thrown = Assert.Throws<InvalidOperationException>(
                () => InterfaceGroupWriter.Duplicate(cache, 44, "my_copy"));

            Assert.Contains("not declared", thrown.Message);
        }
    }
}
