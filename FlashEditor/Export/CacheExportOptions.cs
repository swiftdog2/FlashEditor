using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Cache;

namespace FlashEditor.Export {
    /// <summary>
    ///     What a structured export writes, and where.
    /// </summary>
    /// <remarks>
    ///     Every default here is the cheap one. A whole-cache walk allocates hard - decode buffers
    ///     are not pooled, so the large decodes land on the large object heap - and an export that
    ///     dumped every model, sprite and audio payload by default would be tens of times the size of
    ///     the part anyone can query.
    /// </remarks>
    public sealed class CacheExportOptions {
        /// <summary>The directory name the export lands in, beneath the editor's output directory.</summary>
        public const string DefaultDirectoryName = "cache-export";

        /// <summary>
        ///     Where the export is written, or null to resolve it from <see cref="CachePaths.Output"/>.
        /// </summary>
        /// <remarks>
        ///     Never inside the cache being read. <see cref="CachePaths.Output"/> already refuses to
        ///     resolve to <see cref="CachePaths.Input"/>, and <see cref="ResolveDestination"/> refuses
        ///     any destination inside the open cache's own directory on top of that - the cache is
        ///     read-only, and a few hundred JSON files dropped into it would be indistinguishable
        ///     from cache content to everything that walks the directory afterwards.
        /// </remarks>
        public string? Destination { get; set; }

        /// <summary>
        ///     Whether groups an idx file holds but its reference table does not declare are exported.
        /// </summary>
        /// <remarks>
        ///     Off by default, because the client resolves every read through the table and so cannot
        ///     reach an undeclared group whatever its bytes say. They are listed in each index's
        ///     manifest either way, so an export never silently loses one; this decides whether their
        ///     contents are decoded as well.
        /// </remarks>
        public bool IncludeOrphanGroups { get; set; }

        /// <summary>
        ///     Whether the binary payloads - models, sprites, audio, JPEG, native libraries, shader
        ///     bytecode - are written beside the JSON that describes them.
        /// </summary>
        /// <remarks>
        ///     Off by default. When it is off the manifest still names every payload with its length
        ///     and CRC and no <c>path</c> field at all, rather than naming a file that was never
        ///     written.
        /// </remarks>
        public bool WriteBinaryPayloads { get; set; }

        /// <summary>
        ///     Whether index 7 is decoded far enough to report the emitters, effectors and billboards
        ///     each model attaches.
        /// </summary>
        /// <remarks>
        ///     On, because those references are the only route to two of the joins - which models
        ///     attach a given billboard, and which attach a given particle emitter - and nothing else
        ///     in the cache states them. It costs a full decode of every model in the index; the
        ///     geometry is thrown away immediately and never written.
        /// </remarks>
        public bool IncludeModelReferences { get; set; } = true;

        /// <summary>
        ///     The indexes to export, or null for every index the cache declares a table for.
        /// </summary>
        public IReadOnlyList<int>? Indexes { get; set; }

        /// <summary>
        ///     Where this export will actually be written.
        /// </summary>
        /// <remarks>
        ///     Resolved through <see cref="CachePaths"/> rather than a literal, and checked against
        ///     the cache directory it is being taken from. A destination inside the open cache is
        ///     rejected rather than quietly relocated, because silently writing somewhere other than
        ///     the caller asked for is worse than refusing.
        /// </remarks>
        /// <param name="cacheDirectory">The directory the cache was opened from, or null when unknown.</param>
        /// <returns>The absolute destination directory.</returns>
        /// <exception cref="InvalidOperationException">The destination is inside the open cache.</exception>
        public string ResolveDestination(string? cacheDirectory) {
            string destination = string.IsNullOrWhiteSpace(Destination)
                ? Path.Combine(CachePaths.Output, DefaultDirectoryName)
                : Destination!;

            destination = Path.GetFullPath(destination);

            if (IsInside(destination, cacheDirectory))
                throw new InvalidOperationException(
                    "The export destination " + destination + " is inside the cache being read (" +
                    cacheDirectory + "). The cache is read only, so choose a destination outside it.");

            return destination;
        }

        /// <summary>Whether one directory sits inside another, or is the same directory.</summary>
        /// <param name="candidate">The directory to test.</param>
        /// <param name="root">The directory it must stay out of, or null for no constraint.</param>
        /// <returns>Whether the candidate is inside the root.</returns>
        private static bool IsInside(string candidate, string? root) {
            if (string.IsNullOrWhiteSpace(root))
                return false;

            string full;
            try {
                full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            } catch (ArgumentException) {
                //A malformed cache directory constrains nothing. It cannot be a real parent of the
                //destination, and refusing the export over it would be a failure for the wrong reason.
                return false;
            }

            string trimmed = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(trimmed, full, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
