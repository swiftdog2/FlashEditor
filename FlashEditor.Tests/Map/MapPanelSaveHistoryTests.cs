using System;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     The Map tab's undo history across Save cache, driven through the panel on a copy of the
    ///     real cache.
    /// </summary>
    /// <remarks>
    ///     Works on a copy, because a save commits to disk. The real cache is never written.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class MapPanelSaveHistoryTests : IDisposable
    {
        private const int RegionX = 50, RegionY = 50;

        private readonly string workingCopy;

        public MapPanelSaveHistoryTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            string source = RealCacheLocator.Directory;
            if (source == null)
                return;

            workingCopy = Path.Combine(Path.GetTempPath(), "flasheditor-map-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingCopy);

            foreach (string file in Directory.GetFiles(source, "main_file_cache.*"))
                File.Copy(file, Path.Combine(workingCopy, Path.GetFileName(file)));

            //Into the copy, not beside it: a key file left in a shared place by a run against one
            //cache would silently supply keys to a run against the other.
            string keys = XTEAKeyTable.FindKeyFile(source);
            if (keys != null)
                File.Copy(keys, Path.Combine(workingCopy, Path.GetFileName(keys)), true);
        }

        /// <summary>
        ///     The undo history outlives a save: an undo after a save dirties the square and
        ///     re-enables Save, and the save that follows writes the reverted state.
        /// </summary>
        [RealCacheFact]
        public void SavingKeepsTheHistory_AndSaveIsEnabledExactlyWhenASquareIsDirty()
        {
            int original, edited;

            using (var fileStore = new RSFileStore(workingCopy))
            using (var panel = new MapEditorPanel())
            {
                RSCache cache = Open(fileStore);
                panel.Bind(cache, workingCopy);
                Assert.False(panel.CanSave, "Save is enabled on a freshly opened cache");

                MapRegion square = panel.Store!.GetOrLoad(RegionX, RegionY);
                original = square.GetUnderlayId(0, 10, 10);
                edited = OtherThan(original);

                panel.ApplyEdit(new SetUnderlayEdit(square, 0, 10, 10, edited));
                Assert.True(panel.CanSave);

                Assert.Equal(1, panel.CommitEdits());
                Assert.False(square.Dirty);
                Assert.False(panel.CanSave, "Save is enabled with nothing dirty");
                Assert.True(panel.CanUndo, "the save threw the undo history away");

                panel.Undo();
                Assert.Equal(original, square.GetUnderlayId(0, 10, 10));
                Assert.True(square.Dirty, "an undo after a save left the square clean");
                Assert.True(panel.CanSave, "Save stayed disabled after an undo that follows a save");
                Assert.True(panel.CanRedo);

                //The reverted state is what the second save commits.
                Assert.Equal(1, panel.CommitEdits());
                Assert.False(panel.CanSave);

                panel.Redo();
                Assert.Equal(edited, square.GetUnderlayId(0, 10, 10));
                Assert.True(square.Dirty);
                Assert.True(panel.CanSave);
            }

            //Reopened from disk, because a read through the cache that wrote returns staged bytes
            //whether or not they were committed. The unsaved redo must not have landed.
            using (var fileStore = new RSFileStore(workingCopy))
            {
                MapRegion reread = new MapSquareLoader(Open(fileStore)).Load(RegionX, RegionY, out _);
                Assert.Equal(original, reread.GetUnderlayId(0, 10, 10));
            }
        }

        /// <summary>
        ///     An undo then a redo back to the saved state leaves the square dirty, so the next Save
        ///     rewrites it, but the terrain it would write is byte-identical to what the save
        ///     committed.
        /// </summary>
        /// <remarks>
        ///     Pins the limit of "a save that changes nothing writes nothing" for this flow: the save
        ///     path decides by <c>Region.Dirty</c> and does not compare bytes, so the rewrite still
        ///     re-encodes the container and moves the archive CRC. The payload staying identical is
        ///     what keeps that rewrite harmless to the data. A height edit is included because the
        ///     edited-height flag it sets is not reset by the undo.
        /// </remarks>
        [RealCacheFact]
        public void UndoThenRedoToTheSavedState_IsDirtyButEncodesTheCommittedTerrain()
        {
            using var fileStore = new RSFileStore(workingCopy);
            using var panel = new MapEditorPanel();
            RSCache cache = Open(fileStore);
            panel.Bind(cache, workingCopy);

            MapRegion square = panel.Store!.GetOrLoad(RegionX, RegionY);
            panel.ApplyEdit(ChangeUnderlay(square));
            panel.ApplyEdit(new SetHeightEdit(square, 0, 20, 20,
                square.GetTileHeight(0, 20, 20) - MapRegion.HEIGHT_UNITS_PER_STEP));
            Assert.Equal(1, panel.CommitEdits());

            var loader = new MapSquareLoader(cache);
            byte[] committed = loader.ReadGroupBytes(loader.ResolveGroup(MapSquareNames.Terrain(RegionX, RegionY)));

            panel.Undo();
            panel.Undo();
            panel.Redo();
            panel.Redo();

            Assert.True(square.Dirty);
            Assert.True(panel.CanSave);
            Assert.Equal(committed, RegionCodec.EncodeTerrain(square));
        }

        /// <summary>
        ///     The history is cleared where the squares its edits refer to are gone: unbinding, and
        ///     binding a freshly opened cache.
        /// </summary>
        [RealCacheFact]
        public void BindingAnotherCacheClearsTheHistory()
        {
            using var fileStore = new RSFileStore(workingCopy);
            using var panel = new MapEditorPanel();

            panel.Bind(Open(fileStore), workingCopy);
            MapRegion square = panel.Store!.GetOrLoad(RegionX, RegionY);
            panel.ApplyEdit(ChangeUnderlay(square));
            Assert.True(panel.CanUndo);

            panel.Bind(Open(fileStore), workingCopy);
            Assert.False(panel.CanUndo);
            Assert.False(panel.CanSave);

            square = panel.Store!.GetOrLoad(RegionX, RegionY);
            panel.ApplyEdit(ChangeUnderlay(square));
            panel.Bind(null);
            Assert.False(panel.CanUndo);
            Assert.False(panel.CanSave);
        }

        private static int OtherThan(int underlay) => underlay == 77 ? 78 : 77;

        private static SetUnderlayEdit ChangeUnderlay(MapRegion square) =>
            new SetUnderlayEdit(square, 0, 10, 10, OtherThan(square.GetUnderlayId(0, 10, 10)));

        private RSCache Open(RSFileStore fileStore)
        {
            var cache = new RSCache(fileStore);
            cache.TryAutoLoadXTEAKeys(workingCopy);
            return cache;
        }

        public void Dispose()
        {
            if (workingCopy == null || !Directory.Exists(workingCopy))
                return;

            try
            {
                Directory.Delete(workingCopy, true);
            }
            catch (IOException)
            {
                //A leftover temp copy is untidy, not a failure.
            }
        }
    }
}
