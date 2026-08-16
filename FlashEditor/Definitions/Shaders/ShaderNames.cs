using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;

namespace FlashEditor.Definitions.Shaders {
    /// <summary>
    ///     The names index 31 stores hashes of, at both levels.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Every one of these comes from the client outright rather than from a wordlist, which is
    ///     what makes the join self-proving: the seven file names are the seven the water and
    ///     underwater material classes ask for by name (<c>Class76_Sub9.java:102,105</c>,
    ///     <c>Class76_Sub2.java:122</c>, <c>Class76_Sub8.java:62-68</c> on the <c>gl</c> side;
    ///     <c>Class76_Sub1.java:34,36</c>, <c>Class76_Sub3.java:25</c>,
    ///     <c>Class76_Sub6.java:34-40</c> on the <c>dx</c> side), and both groups carry exactly those
    ///     seven hashes with nothing left over on either side.
    ///     </para>
    ///     <para>
    ///     A name is reported only on exact hash equality, so a candidate that is wrong names nothing
    ///     rather than naming the wrong file - which is the difference between this and the
    ///     track-name join that scored 958 of 970 and was still wrong.
    ///     </para>
    /// </remarks>
    public static class ShaderNames {
        /// <summary>The two rendering backends, which are the group names.</summary>
        private static readonly string[] GroupCandidates = { "gl", "dx" };

        /// <summary>
        ///     The seven shader programs, which are the file names inside either group.
        /// </summary>
        /// <remarks>
        ///     All seven are about water: the transparent surface, its environment-mapped reflection
        ///     as a vertex and a fragment pair, and the four lit and unlit underwater passes over
        ///     ground and over models.
        /// </remarks>
        private static readonly string[] FileCandidates = {
            "transparent_water",
            "environment_mapped_water_v",
            "environment_mapped_water_f",
            "uw_ground_lit",
            "uw_ground_unlit",
            "uw_model_lit",
            "uw_model_unlit"
        };

        private static readonly Dictionary<int, string> GroupsByHash = Index(GroupCandidates);
        private static readonly Dictionary<int, string> FilesByHash = Index(FileCandidates);

        /// <summary>The backend names, so a test can pin what this claims to know.</summary>
        public static IReadOnlyList<string> KnownGroupNames => GroupCandidates;

        /// <summary>The shader program names, so a test can pin what this claims to know.</summary>
        public static IReadOnlyList<string> KnownFileNames => FileCandidates;

        /// <summary>The backend name whose hash is <paramref name="identifier"/>, or null.</summary>
        /// <param name="identifier">The stored group identifier.</param>
        /// <returns>The backend name, or null.</returns>
        public static string? GroupName(int identifier) {
            return GroupsByHash.TryGetValue(identifier, out string? name) ? name : null;
        }

        /// <summary>The shader name whose hash is <paramref name="identifier"/>, or null.</summary>
        /// <param name="identifier">The stored file identifier.</param>
        /// <returns>The shader name, or null.</returns>
        public static string? FileName(int identifier) {
            return FilesByHash.TryGetValue(identifier, out string? name) ? name : null;
        }

        private static Dictionary<int, string> Index(string[] candidates) {
            var index = new Dictionary<int, string>(candidates.Length);
            foreach (string candidate in candidates)
                index[NameHasher.GetNameHash(candidate)] = candidate;
            return index;
        }
    }

    /// <summary>What kind of shader program a stored payload turns out to be.</summary>
    public enum ShaderProgramKind {
        /// <summary>Neither an ARB program, GLSL nor a recognised Direct3D token.</summary>
        Unknown,

        /// <summary>Plaintext ARB assembly, which opens <c>!!ARBvp1.0</c> or <c>!!ARBfp1.0</c>.</summary>
        ArbAssembly,

        /// <summary>Plaintext GLSL - text that is not an ARB program.</summary>
        Glsl,

        /// <summary>Compiled Direct3D 9 bytecode.</summary>
        Direct3DBytecode
    }

    /// <summary>
    ///     What a stored index-31 payload is, read from its own leading bytes.
    /// </summary>
    /// <remarks>
    ///     From the payload and never from the group id. The two groups do split cleanly - <c>gl</c>
    ///     is plaintext and <c>dx</c> is compiled - but deciding from the name would assert that
    ///     split rather than measure it, and the measurement is the useful part.
    /// </remarks>
    public readonly struct ShaderProgramShape {
        private ShaderProgramShape(ShaderProgramKind kind, string description) {
            Kind = kind;
            Description = description;
        }

        /// <summary>Which kind it is.</summary>
        public ShaderProgramKind Kind { get; }

        /// <summary>The kind in words, with the profile where the payload states one.</summary>
        public string Description { get; }

        /// <summary>Whether this payload is plaintext a user could edit.</summary>
        public bool IsSource => Kind == ShaderProgramKind.ArbAssembly || Kind == ShaderProgramKind.Glsl;

        /// <summary>Classifies a stored payload.</summary>
        /// <param name="payload">The stored bytes.</param>
        /// <param name="isText">Whether the payload is printable ASCII throughout.</param>
        /// <returns>What it is.</returns>
        public static ShaderProgramShape Of(ReadOnlySpan<byte> payload, bool isText) {
            if (payload.Length == 0)
                return new ShaderProgramShape(ShaderProgramKind.Unknown, "empty");

            if (isText) {
                //The ARB header is a version line the assembler requires, so it is the format's own
                //statement rather than a heuristic over the text.
                string opening = Encoding.Latin1.GetString(payload.Slice(0, Math.Min(10, payload.Length)));
                if (opening.StartsWith("!!ARB", StringComparison.Ordinal))
                    return new ShaderProgramShape(ShaderProgramKind.ArbAssembly, opening.TrimEnd());

                return new ShaderProgramShape(ShaderProgramKind.Glsl, "GLSL source");
            }

            if (payload.Length < 4)
                return new ShaderProgramShape(ShaderProgramKind.Unknown, "unrecognised");

            /* The Direct3D 9 version token is the first dword, little-endian: FFFE in the high half
               for a vertex shader and FFFF for a pixel shader, with the major and minor version in
               the low two bytes. Every dx file in this cache opens with one. */
            uint token = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            uint kind = token >> 16;
            if (kind == 0xFFFE || kind == 0xFFFF) {
                string profile = (kind == 0xFFFE ? "vs_" : "ps_") +
                                 ((token >> 8) & 0xFF) + "_" + (token & 0xFF);
                return new ShaderProgramShape(ShaderProgramKind.Direct3DBytecode, "D3D9 " + profile);
            }

            return new ShaderProgramShape(ShaderProgramKind.Unknown, "unrecognised binary");
        }
    }

    /// <summary>Convenience lookups over an open cache's index 31.</summary>
    public static class ShaderIndex {
        /// <summary>
        ///     Resolves the address the client uses, <c>"gl"/"transparent_water"</c>.
        /// </summary>
        /// <remarks>
        ///     This is what the per-file name index was built for. <c>JS5Archive.method2739</c>
        ///     lower-cases and hashes both halves and resolves each through the table's identifier
        ///     block, and until <see cref="CacheNameIndex"/> existed the file half of that could not
        ///     be done here at all.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="backend">The backend group, <c>gl</c> or <c>dx</c>.</param>
        /// <param name="shader">The shader program name.</param>
        /// <returns>The stored bytes.</returns>
        public static byte[] Read(RSCache cache, string backend, string shader) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            return cache.ReadFileBytes(RSConstants.GRAPHICS_SHADERS, backend, shader);
        }
    }
}
