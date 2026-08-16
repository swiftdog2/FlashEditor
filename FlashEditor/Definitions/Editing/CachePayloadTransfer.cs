using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     One addressable file in the cache, described well enough to write it to disk or replace
    ///     it from disk.
    /// </summary>
    /// <remarks>
    ///     The whole point of the type is that it says nothing about what the bytes <i>mean</i>.
    ///     Four bespoke export paths existed before this - sprites as PNG, models as OBJ, tracks as
    ///     MIDI, loading sprites as JPEG - and every one of them is a decoder writing its own
    ///     rendering out. None of them can hand you the stored file, which is the only thing that
    ///     works for an index whose payload is a compiled DLL or a shader nobody has a codec for.
    /// </remarks>
    public sealed class CachePayloadTarget {
        /// <summary>Describes one file for transfer.</summary>
        /// <param name="indexId">The index the file lives in.</param>
        /// <param name="address">Where in that index.</param>
        /// <param name="stored">The stored bytes, as read.</param>
        /// <param name="fileName">The name to suggest for a single export, with an extension that suits the payload.</param>
        /// <param name="relativePath">Where the file lands under a chosen folder in a batch export. Defaults to <paramref name="fileName"/>.</param>
        /// <param name="filter">The file dialog filter.</param>
        /// <param name="description">What to call this file in the status line.</param>
        /// <param name="importRefusal">Why this file cannot be replaced, or null when it can.</param>
        /// <param name="validate">Checks an imported file before it is staged, returning a refusal or null.</param>
        public CachePayloadTarget(int indexId, DefinitionAddress address, byte[] stored,
            string fileName, string? relativePath = null,
            string filter = "All files (*.*)|*.*", string? description = null,
            string? importRefusal = null, Func<byte[], string?>? validate = null) {
            IndexId = indexId;
            Address = address;
            Stored = stored ?? throw new ArgumentNullException(nameof(stored));
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            RelativePath = relativePath ?? fileName;
            Filter = filter;
            Description = description ?? address.ToString();
            ImportRefusal = importRefusal;
            Validate = validate;
        }

        /// <summary>The index the file lives in.</summary>
        public int IndexId { get; }

        /// <summary>Where in that index.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The stored bytes, as read.</summary>
        public byte[] Stored { get; }

        /// <summary>The name to suggest for a single export.</summary>
        public string FileName { get; }

        /// <summary>
        ///     Where the file lands under a chosen folder in a batch export.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="FileName"/> because index 30's group names <i>are</i> paths -
        ///     <c>windows/x86/jaggl.dll</c> - and exporting thirty-six libraries into one flat folder
        ///     would collide six ways over on <c>jaggl.dll</c> alone. Rebuilt as directories on the
        ///     way out, which also means the exported tree is the tree the client extracts into.
        /// </remarks>
        public string RelativePath { get; }

        /// <summary>The file dialog filter.</summary>
        public string Filter { get; }

        /// <summary>What to call this file in the status line.</summary>
        public string Description { get; }

        /// <summary>Why this file cannot be replaced, or null when it can.</summary>
        public string? ImportRefusal { get; }

        /// <summary>Checks an imported file before it is staged, returning a refusal or null.</summary>
        public Func<byte[], string?>? Validate { get; }

        /// <summary>Whether an import is offered at all.</summary>
        public bool CanImport => ImportRefusal == null;
    }

    /// <summary>
    ///     Reads a stored payload out to disk and puts one back, for any index.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Bytes only, in both directions, and that is the rule the whole type exists to hold.</b>
    ///     Index 31's seven plaintext shaders are the worked case: four ARB programs use bare LF with
    ///     no CRLF anywhere, <c>transparent_water</c> and both GLSL files use CRLF, and only one of
    ///     the seven ends with a newline at all. A <c>File.WriteAllText</c> / <c>File.ReadAllText</c>
    ///     pair rewrites every one of those silently, and the result still compiles, still looks
    ///     right in an editor, and no longer matches the file nobody edited. So exports go through
    ///     <see cref="File.WriteAllBytes"/> and imports through <see cref="File.ReadAllBytes"/>,
    ///     here, once, rather than being re-decided per tab.
    ///     </para>
    ///     <para>
    ///     <b>An import that changes nothing writes nothing.</b> Re-storing identical bytes still
    ///     re-encodes the container, which changes the archive CRC and drags in the reference-table
    ///     entry of every archive packed beside it - and on index 30 it rewrites a whirlpool digest
    ///     too. <see cref="RSCache.WriteFile"/> has its own unchanged path, but the comparison is
    ///     repeated here so the user is told the file was identical instead of being told a write
    ///     was staged when none was.
    ///     </para>
    /// </remarks>
    public static class CachePayloadTransfer {
        /// <summary>What one transfer did, and what to say about it.</summary>
        /// <param name="Changed">Whether anything was staged or written.</param>
        /// <param name="Message">A line for the status label.</param>
        public readonly record struct Outcome(bool Changed, string Message);

        /// <summary>
        ///     Writes one stored payload to a path, byte for byte.
        /// </summary>
        /// <param name="target">The file to export.</param>
        /// <param name="path">Where to write it.</param>
        /// <returns>What happened.</returns>
        public static Outcome Export(CachePayloadTarget target, string path) {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            File.WriteAllBytes(path, target.Stored);
            return new Outcome(true,
                "Wrote " + Count(target.Stored.Length) + " to " + Path.GetFileName(path));
        }

        /// <summary>
        ///     Writes every payload under a folder, rebuilding each one's relative path as directories.
        /// </summary>
        /// <remarks>
        ///     The path is rebuilt rather than flattened because on index 30 it is the only thing
        ///     that distinguishes six identically-named libraries, and because the resulting tree is
        ///     what the client itself extracts. Each segment is sanitised: a name comes out of a
        ///     32-bit hash match and nothing guarantees it is a legal Windows path.
        /// </remarks>
        /// <param name="targets">The files to export.</param>
        /// <param name="folder">The folder to write under.</param>
        /// <returns>What happened.</returns>
        public static Outcome ExportAll(IReadOnlyList<CachePayloadTarget> targets, string folder) {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));
            if (folder == null)
                throw new ArgumentNullException(nameof(folder));

            long bytes = 0;
            foreach (CachePayloadTarget target in targets) {
                string path = Path.Combine(folder, SafeRelativePath(target.RelativePath));
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(path, target.Stored);
                bytes += target.Stored.Length;
            }

            return new Outcome(targets.Count > 0,
                "Wrote " + targets.Count + " file(s), " + Count(bytes) + ", under " + folder);
        }

        /// <summary>
        ///     Stages a file from disk over a stored payload, unless it is the payload already there.
        /// </summary>
        /// <remarks>
        ///     Staged, not saved. <see cref="RSCache.WriteFile"/> writes into the in-memory overlay
        ///     and nothing reaches the filesystem until the cache is saved, which is what the message
        ///     says so a user does not go looking for a changed dat2.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="target">The file being replaced.</param>
        /// <param name="path">The file to read.</param>
        /// <returns>What happened.</returns>
        public static Outcome Import(RSCache cache, CachePayloadTarget target, string path) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            if (!target.CanImport)
                return new Outcome(false, "Refused: " + target.ImportRefusal);

            byte[] bytes = File.ReadAllBytes(path);
            return Stage(cache, target, bytes, Path.GetFileName(path));
        }

        /// <summary>
        ///     Stages bytes that are already in hand - an edited text buffer rather than a file.
        /// </summary>
        /// <remarks>
        ///     Split out of <see cref="Import"/> so an in-tab editor takes exactly the same
        ///     unchanged-payload check that a file import does. A second staging path would be a
        ///     second place for that check to be left out, and it is the check that decides whether
        ///     a no-op edit rewrites an archive.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="target">The file being replaced.</param>
        /// <param name="bytes">The replacement payload.</param>
        /// <param name="source">What produced the bytes, for the status line.</param>
        /// <returns>What happened.</returns>
        public static Outcome Stage(RSCache cache, CachePayloadTarget target, byte[] bytes, string source) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (!target.CanImport)
                return new Outcome(false, "Refused: " + target.ImportRefusal);

            if (bytes.AsSpan().SequenceEqual(target.Stored))
                return new Outcome(false,
                    "No change: " + source + " is byte for byte what " + target.Description +
                    " already holds, so nothing was staged.");

            string? refusal = target.Validate?.Invoke(bytes);
            if (refusal != null)
                return new Outcome(false, "Refused: " + refusal);

            cache.WriteFile(target.IndexId, target.Address.GroupId, target.Address.FileId, new JagStream(bytes));

            return new Outcome(true,
                "Staged " + target.Description + ": " + Count(bytes.Length) +
                " stored verbatim, was " + Count(target.Stored.Length) +
                ". Nothing reaches disk until the cache is saved.");
        }

        /// <summary>
        ///     A relative path with every segment made legal, and with no way out of the chosen folder.
        /// </summary>
        /// <remarks>
        ///     A name here is whatever string hashed to a stored identifier, so it is not trusted to
        ///     be a path: rooted segments, drive letters and <c>..</c> are all stripped rather than
        ///     escaped, because an export must not be able to write outside the folder the user
        ///     picked.
        /// </remarks>
        /// <param name="relativePath">The path a target asked for.</param>
        /// <returns>A safe relative path.</returns>
        private static string SafeRelativePath(string relativePath) {
            string[] segments = relativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
            var safe = new List<string>(segments.Length);

            foreach (string segment in segments) {
                if (segment == "." || segment == "..")
                    continue;

                var cleaned = new char[segment.Length];
                for (int i = 0; i < segment.Length; i++)
                    cleaned[i] = Array.IndexOf(Path.GetInvalidFileNameChars(), segment[i]) >= 0 ? '_' : segment[i];

                string text = new string(cleaned).Trim();
                if (text.Length > 0)
                    safe.Add(text);
            }

            return safe.Count == 0 ? "unnamed" : Path.Combine(safe.ToArray());
        }

        private static string Count(long bytes) {
            return bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
        }
    }
}
