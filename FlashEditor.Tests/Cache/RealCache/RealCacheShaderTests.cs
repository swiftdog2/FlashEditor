using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     Index 31 against the shipped bytes: the name join, the two payload families, and the
    ///     line-ending profile a text editor has to reproduce.
    /// </summary>
    /// <remarks>
    ///     Read only. The edit path is proved separately, in <c>RealCacheShaderEditTests</c>, against
    ///     a copy.
    /// </remarks>
    public class RealCacheShaderTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        /// <param name="cache">The shared cache fixture.</param>
        /// <param name="output">The test output sink.</param>
        public RealCacheShaderTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        private RSReferenceTable Table => _cache.Table(RSConstants.GRAPHICS_SHADERS);

        /// <summary>Every declared file, with its recovered names and its decoded document.</summary>
        /// <returns>The whole index, in group then file order.</returns>
        private List<(int Group, int File, string Backend, string Shader, ShaderTextDocument Document)> Everything()
        {
            RSCache cache = _cache.OpenCache();
            var all = new List<(int, int, string, string, ShaderTextDocument)>();

            foreach (KeyValuePair<int, RSArchiveEntry> group in Table.GetArchiveEntries())
            {
                foreach (int fileId in group.Value.GetValidFileIds())
                {
                    byte[] payload = cache.ReadFileBytes(RSConstants.GRAPHICS_SHADERS, group.Key, fileId);
                    all.Add((group.Key, fileId,
                        ShaderNames.GroupName(group.Value.GetIdentifier()),
                        ShaderNames.FileName(group.Value.GetFileEntry(fileId).GetIdentifier()),
                        ShaderTextDocument.Decode(payload)));
                }
            }

            return all;
        }

        /// <summary>
        ///     Every stored identifier resolves against a name the client asks for, both ways.
        /// </summary>
        /// <remarks>
        ///     A self-proving join. The candidate names are not from a wordlist - they are the names
        ///     the water and underwater material classes pass to <c>JS5Archive.method2739</c> - and
        ///     the assertion is that nothing is unmatched on either side, which coverage alone would
        ///     not give. The track-name join is the cautionary case: 958 of 970 and wrong.
        /// </remarks>
        [RealCacheFact]
        public void EveryNameTheClientAsksForResolvesAndNothingIsLeftOver()
        {
            var unresolved = new List<string>();
            var seen = new List<string>();

            foreach ((int group, int file, string backend, string shader, _) in Everything())
            {
                if (backend == null)
                    unresolved.Add($"group {group} has no recovered backend name");
                if (shader == null)
                    unresolved.Add($"group {group} file {file} has no recovered shader name");
                if (backend != null && shader != null)
                    seen.Add(backend + "/" + shader);
            }

            foreach (string address in seen.OrderBy(text => text, StringComparer.Ordinal))
                _output.WriteLine(address);

            Assert.Empty(unresolved);
            Assert.Equal(_cache.DeclaredFiles(RSConstants.GRAPHICS_SHADERS), seen.Count);

            //Nothing left over the other way either: every committed shader name is used in every
            //backend, so a name that had drifted out of the cache would show up here rather than
            //quietly reducing coverage.
            foreach (int groupId in Table.GetArchiveEntries().Keys)
            {
                string backend = ShaderNames.GroupName(Table.GetArchiveEntry(groupId).GetIdentifier());
                foreach (string shader in ShaderNames.KnownFileNames)
                    Assert.Contains(backend + "/" + shader, seen);
            }
        }

        /// <summary>
        ///     Reading by the two-part name the client uses returns the same bytes as reading by id.
        /// </summary>
        /// <remarks>
        ///     This is what the per-file name index was built for. Before it,
        ///     <c>"gl"/"transparent_water"</c> could not be resolved at all - the group half worked
        ///     and the file half had no lookup behind it.
        /// </remarks>
        [RealCacheFact]
        public void ReadingByNameReturnsWhatReadingByIdReturns()
        {
            RSCache cache = _cache.OpenCache();
            int compared = 0;

            foreach ((int group, int file, string backend, string shader, ShaderTextDocument document)
                     in Everything())
            {
                if (backend == null || shader == null)
                    continue;

                Assert.Equal(document.Original, ShaderIndex.Read(cache, backend, shader));
                Assert.Equal((group, file), Resolve(cache, backend, shader));
                compared++;
            }

            Assert.Equal(_cache.DeclaredFiles(RSConstants.GRAPHICS_SHADERS), compared);
        }

        private static (int, int) Resolve(RSCache cache, string backend, string shader)
        {
            cache.GetNameIndex(RSConstants.GRAPHICS_SHADERS)
                .TryResolve(backend, shader, out int group, out int file);
            return (group, file);
        }

        /// <summary>
        ///     One backend is entirely plaintext and the other entirely compiled.
        /// </summary>
        /// <remarks>
        ///     Classified from each payload's own bytes, then checked against the backend name -
        ///     which is the only order that makes the agreement a measurement. Deciding the split
        ///     from the group id would assert it instead, and is the mistake the loading-sprites tab
        ///     exists to correct on index 32.
        /// </remarks>
        [RealCacheFact]
        public void ThePlaintextBackendIsTheOneNamedGl()
        {
            var byBackend = new SortedDictionary<string, List<ShaderProgramShape>>(StringComparer.Ordinal);

            foreach ((_, _, string backend, _, ShaderTextDocument document) in Everything())
            {
                ShaderProgramShape shape = ShaderProgramShape.Of(document.Original, document.IsText);
                if (!byBackend.TryGetValue(backend, out List<ShaderProgramShape> shapes))
                    byBackend[backend] = shapes = new List<ShaderProgramShape>();
                shapes.Add(shape);
            }

            foreach (KeyValuePair<string, List<ShaderProgramShape>> backend in byBackend)
                _output.WriteLine(backend.Key + ": " +
                                  string.Join(", ", backend.Value.Select(shape => shape.Description)
                                      .GroupBy(text => text)
                                      .Select(family => family.Count() + " " + family.Key)));

            Assert.True(byBackend.ContainsKey("gl"), "no backend named gl");
            Assert.True(byBackend.ContainsKey("dx"), "no backend named dx");

            Assert.All(byBackend["gl"], shape => Assert.True(shape.IsSource,
                "a gl shader is not plaintext: " + shape.Description));
            Assert.All(byBackend["dx"], shape =>
                Assert.Equal(ShaderProgramKind.Direct3DBytecode, shape.Kind));

            //Both ARB and GLSL occur in the gl group, so a classifier that reported one kind for all
            //seven would fail rather than pass with a plausible answer.
            Assert.Contains(byBackend["gl"], shape => shape.Kind == ShaderProgramKind.ArbAssembly);
            Assert.Contains(byBackend["gl"], shape => shape.Kind == ShaderProgramKind.Glsl);
        }

        /// <summary>
        ///     Every plaintext shader re-encodes to the bytes it was read from.
        /// </summary>
        /// <remarks>
        ///     <b>The claim the whole shader tab rests on.</b> The display text is CRLF whatever the
        ///     file uses, so this is the assertion that the recorded convention really does put the
        ///     file back as it was - and it is asserted against the stored bytes rather than against
        ///     a re-decode, because round-tripping an encoder against its own decoder proves nothing.
        /// </remarks>
        [RealCacheFact]
        public void EveryPlaintextShaderReEncodesToItsStoredBytes()
        {
            var failures = new List<string>();
            int checkedFiles = 0;

            foreach ((int group, int file, _, string shader, ShaderTextDocument document) in Everything())
            {
                if (!document.IsText)
                    continue;

                checkedFiles++;

                if (!document.RoundTripsExactly)
                {
                    failures.Add($"group {group} file {file} ({shader}) does not survive a text round trip");
                    continue;
                }

                if (!document.Encode(document.DisplayText).AsSpan().SequenceEqual(document.Original))
                    failures.Add($"group {group} file {file} ({shader}) re-encodes to different bytes");
            }

            _output.WriteLine($"{checkedFiles} plaintext shader(s) re-encoded to their stored bytes");

            Assert.Empty(failures);
            Assert.True(checkedFiles > 0, "no plaintext shader was checked, so this asserted nothing");
        }

        /// <summary>
        ///     The line-ending conventions are not uniform, which is why they are recorded per file.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     A decoder that hardcoded either convention would pass the re-encode sweep on the half
        ///     of the index it happened to guess right. This is the assertion it cannot pass: both
        ///     conventions occur, both trailing-newline states occur, and no file mixes them - so the
        ///     recorded value is doing real work on every file.
        ///     </para>
        ///     <para>
        ///     The exact split is measured rather than asserted from a document, and recorded through
        ///     the profile census: it is a property of the shipped files, and a figure written here
        ///     would be read as a target.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void BothLineEndingConventionsOccurAndNoFileMixesThem()
        {
            var mixed = new List<string>();
            int lf = 0;
            int crlf = 0;
            int trailing = 0;
            int text = 0;

            foreach ((int group, int file, _, string shader, ShaderTextDocument document) in Everything())
            {
                if (!document.IsText)
                    continue;

                text++;

                switch (document.Ending)
                {
                    case ShaderLineEnding.Lf:
                        lf++;
                        break;
                    case ShaderLineEnding.CrLf:
                        crlf++;
                        break;
                    case ShaderLineEnding.Mixed:
                        mixed.Add($"group {group} file {file} ({shader})");
                        break;
                }

                if (document.EndsWithNewline)
                    trailing++;

                _output.WriteLine($"{shader}: {document.EndingText}, " +
                                  (document.EndsWithNewline ? "ends with a newline" : "no trailing newline"));
            }

            Assert.Empty(mixed);
            Assert.True(lf > 0, "no plaintext shader uses bare LF, so the LF branch is untested here");
            Assert.True(crlf > 0, "no plaintext shader uses CRLF, so the CRLF branch is untested here");

            //Both states of the trailing newline occur, so neither adding one nor stripping one can
            //pass. This is the half that an editor "tidying up" would break.
            Assert.True(trailing > 0, "no plaintext shader ends with a newline");
            Assert.True(trailing < text, "every plaintext shader ends with a newline");

            _cache.Profile.AssertCensus(_output, "shader.plaintextFiles", text);
            _cache.Profile.AssertCensus(_output, "shader.bareLfFiles", lf);
            _cache.Profile.AssertCensus(_output, "shader.crlfFiles", crlf);
            _cache.Profile.AssertCensus(_output, "shader.filesEndingWithANewline", trailing);
        }
    }
}
