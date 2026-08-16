using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Shaders;
using FlashEditor.Utils;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     The shader edit path end to end, against a copy of the cache.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The order of the phases is the test.</b> Every plaintext shader is first staged
    ///     unedited and required to stage <i>nothing</i>; only then is one actually edited and
    ///     required to persist; and only then is the edit undone and required to land on the original
    ///     bytes. An editor that rewrote unconditionally would pass the second phase and fail the
    ///     first, and the second on its own would read as success - which is how a line-ending
    ///     rewrite gets shipped.
    ///     </para>
    ///     <para>
    ///     Persistence is checked by <b>reopening the store</b>. A read through the
    ///     <see cref="RSCache"/> that wrote returns the new bytes whether or not they were committed,
    ///     so reading back through it would prove nothing about the save.
    ///     </para>
    ///     <para>
    ///     Staged through <see cref="CachePayloadTransfer"/>, which is the path the tab uses, rather
    ///     than through <c>RSCache.WriteFile</c> directly. The unchanged-payload check lives there,
    ///     and a test that bypassed it would be testing a route no user takes.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheShaderEditTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string workingCopy;
        private readonly bool available;

        /// <summary>Takes a private copy of the cache to write into.</summary>
        /// <param name="output">The test output sink.</param>
        public RealCacheShaderEditTests(ITestOutputHelper output)
        {
            _output = output;
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            string source = RealCacheLocator.Directory;
            if (source == null)
                return;

            workingCopy = Path.Combine(Path.GetTempPath(), "flasheditor-shader-edit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingCopy);

            foreach (string file in Directory.GetFiles(source, "main_file_cache.*"))
                File.Copy(file, Path.Combine(workingCopy, Path.GetFileName(file)));

            //Index 31 is never encrypted, but the copy is a whole cache and other indexes in it are.
            //Copied into the working copy for the reason the map tests copy it there: a shared key
            //directory would let a run against one cache supply keys to a run against the other.
            string keys = XTEAKeyTable.FindKeyFile(source);
            if (keys != null)
                File.Copy(keys, Path.Combine(workingCopy, Path.GetFileName(keys)), true);

            available = true;
        }

        [RealCacheFact]
        public void StagingAnUneditedShaderWritesNothingAndAnUndoneEditLandsOnTheOriginalBytes()
        {
            if (!available)
                return;

            int group;
            int file;
            byte[] original;
            ShaderLineEnding originalEnding;
            bool originalTrailingNewline;

            /* ---- phase one: staging an unedited shader must write nothing ------------------ */
            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                RSReferenceTable table = cache.GetReferenceTable(RSConstants.GRAPHICS_SHADERS);

                var plaintext = new List<(int Group, int File, ShaderTextDocument Document)>();

                foreach (KeyValuePair<int, RSArchiveEntry> entry in table.GetArchiveEntries())
                {
                    foreach (int fileId in entry.Value.GetValidFileIds())
                    {
                        ShaderTextDocument document = ShaderTextDocument.Decode(
                            cache.ReadFileBytes(RSConstants.GRAPHICS_SHADERS, entry.Key, fileId));

                        if (document.IsText)
                            plaintext.Add((entry.Key, fileId, document));
                    }
                }

                Assert.NotEmpty(plaintext);
                Assert.False(cache.HasUnsavedChanges, "reading staged a change");

                foreach ((int groupId, int fileId, ShaderTextDocument document) in plaintext)
                {
                    CachePayloadTransfer.Outcome outcome = CachePayloadTransfer.Stage(cache,
                        Target(groupId, fileId, document.Original),
                        document.Encode(document.DisplayText), "the editor");

                    Assert.False(outcome.Changed,
                        $"group {groupId} file {fileId} staged a change with nothing edited: {outcome.Message}");
                    Assert.Contains("No change", outcome.Message);
                }

                Assert.False(cache.HasUnsavedChanges,
                    "staging every plaintext shader unedited left a change staged");

                //A bare-LF file, so the phases below prove that convention specifically survives an
                //edit. Chosen by measurement: which file uses which convention is a property of the
                //cache and not something to write down here.
                (int Group, int File, ShaderTextDocument Document) chosen =
                    plaintext.FirstOrDefault(row => row.Document.Ending == ShaderLineEnding.Lf);

                Assert.True(chosen.Document != null, "no bare-LF shader in this cache to edit");

                group = chosen.Group;
                file = chosen.File;
                original = chosen.Document.Original;
                originalEnding = chosen.Document.Ending;
                originalTrailingNewline = chosen.Document.EndsWithNewline;

                _output.WriteLine($"editing group {group} file {file}: {original.Length:N0} bytes, " +
                                  $"{chosen.Document.EndingText}, " +
                                  (originalTrailingNewline ? "trailing newline" : "no trailing newline"));
            }

            /* ---- phase two: a real edit persists, in the file's own convention -------------- */
            byte[] edited;

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                ShaderTextDocument document = ShaderTextDocument.Decode(
                    cache.ReadFileBytes(RSConstants.GRAPHICS_SHADERS, group, file));

                //Typed the way the text box would hand it back - CRLF - so the encode has to convert
                //it to the file's own LF rather than storing what the control produced. An edit
                //written in the stored convention would not test that at all.
                edited = document.Encode(document.DisplayText + "\r\n# flasheditor round trip");

                CachePayloadTransfer.Outcome outcome = CachePayloadTransfer.Stage(cache,
                    Target(group, file, document.Original), edited, "the editor");

                Assert.True(outcome.Changed, "an edited shader staged nothing: " + outcome.Message);
                Assert.True(cache.HasUnsavedChanges);

                cache.WriteCache(workingCopy);
                Assert.False(cache.HasUnsavedChanges);
            }

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                byte[] persisted = cache.ReadFileBytes(RSConstants.GRAPHICS_SHADERS, group, file);

                Assert.Equal(edited, persisted);

                //The edit was typed in CRLF and the file was bare LF, so this is the assertion that
                //the tab does not convert a file by editing it.
                ShaderTextDocument reread = ShaderTextDocument.Decode(persisted);
                Assert.Equal(originalEnding, reread.Ending);
                Assert.Equal(originalTrailingNewline, reread.EndsWithNewline);
                Assert.DoesNotContain((byte) '\r', persisted);
            }

            /* ---- phase three: undo the edit and land on the original bytes ------------------ */
            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                ShaderTextDocument document = ShaderTextDocument.Decode(
                    cache.ReadFileBytes(RSConstants.GRAPHICS_SHADERS, group, file));

                string reverted = document.DisplayText.Replace("\r\n# flasheditor round trip", string.Empty);

                CachePayloadTransfer.Outcome outcome = CachePayloadTransfer.Stage(cache,
                    Target(group, file, document.Original), document.Encode(reverted), "the editor");

                Assert.True(outcome.Changed, "undoing the edit staged nothing: " + outcome.Message);

                cache.WriteCache(workingCopy);
            }

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);

                //Byte for byte what it started as. The stored container is a different, equally valid
                //GZip encoding of these bytes - a GZip re-encode is never byte-identical - so the
                //payload is the only thing that can be compared, and it is the thing that matters.
                Assert.Equal(original,
                    cache.ReadFileBytes(RSConstants.GRAPHICS_SHADERS, group, file));
            }
        }

        /// <summary>Describes one shader file for the transfer surface.</summary>
        /// <param name="group">The group id.</param>
        /// <param name="file">The file id within it.</param>
        /// <param name="stored">The bytes currently stored, which is what a no-op is measured against.</param>
        /// <returns>The target.</returns>
        private static CachePayloadTarget Target(int group, int file, byte[] stored)
        {
            return new CachePayloadTarget(RSConstants.GRAPHICS_SHADERS,
                new DefinitionAddress(group, file), stored,
                "shader.txt", description: "group " + group + " file " + file);
        }

        /// <summary>Removes the working copy.</summary>
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
