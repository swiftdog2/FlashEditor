using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.LoadingScreens {
    /// <summary>
    ///     One row of the manifest: the screens available to a category, and whether they are shuffled.
    /// </summary>
    public sealed class LoadingScreenCategory {
        /// <summary>Which category slot this row fills.</summary>
        /// <remarks>
        ///     Rows index into an array sized from the manifest's own maximum, so the stored order of
        ///     the rows and their indexes are independent - which is why the rows are a list rather
        ///     than an array addressed by this.
        /// </remarks>
        public int Index { get; set; }

        /// <summary>
        ///     The stored shuffle byte, which the client turns into a bool by comparing it to 1.
        /// </summary>
        /// <remarks>
        ///     Class282.java:102 spells it <c>(readUnsignedByte() ^ 0xffffffff) == -2</c>, so every
        ///     value but 1 means false. Kept as the byte so a stored 2 does not re-encode as 0.
        /// </remarks>
        public int ShuffleStored { get; set; }

        /// <summary>Whether the client shuffles this category's screens.</summary>
        public bool Shuffles => ShuffleStored == 1;

        /// <summary>File ids in group 1, in the order the manifest lists them.</summary>
        public int[] ScreenIds { get; set; } = Array.Empty<int>();

        /// <summary>Takes an independent copy.</summary>
        /// <returns>A row holding the same values.</returns>
        public LoadingScreenCategory Clone() {
            return new LoadingScreenCategory {
                Index = Index,
                ShuffleStored = ShuffleStored,
                ScreenIds = (int[]) ScreenIds.Clone()
            };
        }
    }

    /// <summary>
    ///     The single file in group 0 of JS5 index 33: which loading screens each category may show.
    /// </summary>
    /// <remarks>
    ///     Decoded by the <c>Class282</c> constructor (Class282.java:64-136) from
    ///     <c>getChildFromFolder(0, 0)</c>.
    ///     <para>
    ///     <b>The type-version block is a compatibility handshake that fails silently.</b> If its
    ///     count or any of its bytes disagrees with the client's own table, Class282.java:86-89
    ///     empties both arrays and the client shows no loading screen at all, with no error. It is
    ///     therefore stored and replayed verbatim rather than regenerated from
    ///     <see cref="LoadingScreenElement.ClientTypeVersions"/> - the client this cache is read
    ///     beside is 637 and the cache is 639, so the two are not guaranteed to agree, and
    ///     regenerating would silently rewrite the file to match the wrong build.
    ///     </para>
    /// </remarks>
    public sealed class LoadingScreenManifest {
        /// <summary>The group the manifest lives in.</summary>
        public const int GroupId = 0;

        /// <summary>The file within that group.</summary>
        public const int FileId = 0;

        /// <summary>Highest version the 637 client parses; above it, it reads nothing at all.</summary>
        /// <remarks>Class282.java:71 gates the whole parse on <c>version &lt;= 3</c>.</remarks>
        public const int MaxParsedVersion = 3;

        /// <summary>Lowest version that stores <see cref="DefaultScreenId"/>.</summary>
        /// <remarks>Class282.java:93-97 leaves it at -1 below this.</remarks>
        public const int DefaultScreenIdVersion = 3;

        /// <summary>The format version byte.</summary>
        public int Version { get; set; } = MaxParsedVersion;

        /// <summary>
        ///     The per-type version bytes, as stored.
        /// </summary>
        /// <remarks>
        ///     Length is itself stored, ahead of the bytes, and the client requires it to equal its
        ///     own type count. Kept verbatim; see the type note on this class.
        /// </remarks>
        public int[] TypeVersions { get; set; } = Array.Empty<int>();

        /// <summary>
        ///     The highest category index the client sizes its arrays for.
        /// </summary>
        /// <remarks>
        ///     Stored separately from the row count and not derivable from it: Class282.java:98-99
        ///     allocates <c>this + 1</c> slots and :117-125 fills every slot no row named, so a
        ///     manifest may legitimately declare more slots than it has rows.
        /// </remarks>
        public int MaxCategoryIndex { get; set; }

        /// <summary>
        ///     Screen prepended to every category, or -1 for none.
        /// </summary>
        /// <remarks>
        ///     Load-bearing beyond its own value: when it is not -1, Class282.java:110-113 prepends it
        ///     to every category list and :153 makes the shuffle skip slot 0, so the same category
        ///     bytes mean different things depending on this one field. It is -1 in both supported
        ///     caches, so nothing here exercises the other branch.
        /// </remarks>
        public int DefaultScreenId { get; set; } = -1;

        /// <summary>The category rows, in stored order.</summary>
        public List<LoadingScreenCategory> Categories { get; } = new List<LoadingScreenCategory>();

        /// <summary>
        ///     Bytes past the point the 637 client stops reading, kept so they survive a save.
        /// </summary>
        /// <remarks>
        ///     Non-empty only for a version above <see cref="MaxParsedVersion"/>, where the client
        ///     reads the version byte and nothing else. Neither supported cache has one, so this is
        ///     the same defence the reference-table codec makes for its unreachable branches: a
        ///     format the reader does not understand is carried rather than dropped.
        /// </remarks>
        public byte[] UnparsedTail { get; set; } = Array.Empty<byte>();

        /// <summary>Reads the manifest.</summary>
        /// <param name="stream">The file, positioned at its first byte.</param>
        /// <returns>This definition.</returns>
        public LoadingScreenManifest Decode(JagStream stream) {
            Categories.Clear();
            TypeVersions = Array.Empty<int>();
            UnparsedTail = Array.Empty<byte>();
            MaxCategoryIndex = 0;
            DefaultScreenId = -1;

            Version = stream.ReadUnsignedByte();
            if (Version > MaxParsedVersion) {
                //Mirrors Class282.java:127-131, which reads the version and abandons the file.
                UnparsedTail = stream.ReadBytes(stream.Remaining());
                return this;
            }

            int typeCount = stream.ReadUnsignedByte();
            TypeVersions = new int[typeCount];
            for (int i = 0; i < typeCount; i++)
                TypeVersions[i] = stream.ReadUnsignedByte();

            int rows = stream.ReadUnsignedByte();
            MaxCategoryIndex = stream.ReadUnsignedByte();

            if (Version >= DefaultScreenIdVersion)
                DefaultScreenId = stream.ReadShort();

            for (int i = 0; i < rows; i++) {
                var category = new LoadingScreenCategory {
                    Index = stream.ReadUnsignedByte(),
                    ShuffleStored = stream.ReadUnsignedByte()
                };

                int screens = stream.ReadUnsignedShort();
                int[] ids = new int[screens];
                for (int j = 0; j < screens; j++)
                    ids[j] = stream.ReadUnsignedShort();
                category.ScreenIds = ids;

                Categories.Add(category);
            }

            return this;
        }

        /// <summary>Writes the manifest back to the file representation.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var stream = new JagStream();
            stream.WriteByte((byte) Version);

            if (Version > MaxParsedVersion) {
                stream.Write(UnparsedTail, 0, UnparsedTail.Length);
                return stream.Flip();
            }

            if (TypeVersions.Length > byte.MaxValue || Categories.Count > byte.MaxValue)
                throw new InvalidOperationException(
                    "The manifest's type-version count and category-row count are single bytes.");

            stream.WriteByte((byte) TypeVersions.Length);
            foreach (int version in TypeVersions)
                stream.WriteByte((byte) version);

            stream.WriteByte((byte) Categories.Count);
            stream.WriteByte((byte) MaxCategoryIndex);

            if (Version >= DefaultScreenIdVersion)
                stream.WriteShort(DefaultScreenId);

            foreach (LoadingScreenCategory category in Categories) {
                stream.WriteByte((byte) category.Index);
                stream.WriteByte((byte) category.ShuffleStored);
                stream.WriteShort(category.ScreenIds.Length);
                foreach (int screenId in category.ScreenIds)
                    stream.WriteShort(screenId);
            }

            return stream.Flip();
        }

        /// <summary>Takes a copy no edit through this instance can reach.</summary>
        /// <returns>An independent manifest holding the same values.</returns>
        public LoadingScreenManifest Clone() {
            var copy = new LoadingScreenManifest {
                Version = Version,
                TypeVersions = (int[]) TypeVersions.Clone(),
                MaxCategoryIndex = MaxCategoryIndex,
                DefaultScreenId = DefaultScreenId,
                UnparsedTail = (byte[]) UnparsedTail.Clone()
            };

            foreach (LoadingScreenCategory category in Categories)
                copy.Categories.Add(category.Clone());

            return copy;
        }
    }
}
