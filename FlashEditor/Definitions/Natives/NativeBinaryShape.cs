using System;
using System.Buffers.Binary;

namespace FlashEditor.Definitions.Natives {
    /// <summary>Which executable container a native library payload is.</summary>
    public enum NativeBinaryKind {
        /// <summary>None of the magics below matched.</summary>
        Unknown,

        /// <summary>A Windows PE image, which opens <c>MZ</c>.</summary>
        PortableExecutable,

        /// <summary>An ELF shared object, which opens <c>7F 45 4C 46</c>.</summary>
        Elf,

        /// <summary>A single-architecture Mach-O image.</summary>
        MachO,

        /// <summary>A Mach-O fat binary carrying several architectures.</summary>
        MachOUniversal
    }

    /// <summary>
    ///     What a stored native library payload turns out to be, read from its own leading bytes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Read from the payload rather than inferred from the group name, and the two are shown side
    ///     by side on purpose. The name is a claim the cache makes about a binary; the magic is what
    ///     the binary is. They agree on every group in both caches, and a tab that derived one from
    ///     the other could not have told you that.
    ///     </para>
    ///     <para>
    ///     The word width matters more than it looks. One group is named <c>windows/x64/</c> where
    ///     every other 64-bit Windows library is under <c>windows/x86_64/</c>, and the PE machine
    ///     word is what settles that it really is the 64-bit build rather than a misfiled 32-bit one.
    ///     </para>
    /// </remarks>
    public readonly struct NativeBinaryShape {
        private NativeBinaryShape(NativeBinaryKind kind, string format, string architecture, int bits) {
            Kind = kind;
            Format = format;
            Architecture = architecture;
            Bits = bits;
        }

        /// <summary>Which container it is.</summary>
        public NativeBinaryKind Kind { get; }

        /// <summary>The container in words, for a column.</summary>
        public string Format { get; }

        /// <summary>The architecture the header names, or "unknown".</summary>
        public string Architecture { get; }

        /// <summary>The word width the header states, or 0 when it states none this reads.</summary>
        public int Bits { get; }

        /// <summary>The word width as a column value, blank when unknown.</summary>
        public string BitsText => Bits == 0 ? string.Empty : Bits + "-bit";

        /// <summary>Classifies a stored payload.</summary>
        /// <param name="payload">The stored bytes.</param>
        /// <returns>What the leading bytes say it is.</returns>
        public static NativeBinaryShape Of(ReadOnlySpan<byte> payload) {
            if (payload.Length < 4)
                return new NativeBinaryShape(NativeBinaryKind.Unknown, "empty", "unknown", 0);

            if (payload[0] == 0x4D && payload[1] == 0x5A)
                return PortableExecutable(payload);

            if (payload[0] == 0x7F && payload[1] == 0x45 && payload[2] == 0x4C && payload[3] == 0x46)
                return Elf(payload);

            //Read big-endian throughout: a Mach-O header is stored in the target's byte order, so the
            //little-endian images read back as the byte-swapped constants rather than needing a
            //separate probe.
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(payload);
            return magic switch {
                0xCAFEBABE or 0xCAFEBABF => Universal(payload),
                0xFEEDFACE => MachO(payload, bits: 32, littleEndian: false),
                0xFEEDFACF => MachO(payload, bits: 64, littleEndian: false),
                0xCEFAEDFE => MachO(payload, bits: 32, littleEndian: true),
                0xCFFAEDFE => MachO(payload, bits: 64, littleEndian: true),
                _ => new NativeBinaryShape(NativeBinaryKind.Unknown, "unrecognised", "unknown", 0)
            };
        }

        /// <summary>
        ///     Reads the COFF machine word a PE image's optional header is preceded by.
        /// </summary>
        /// <remarks>
        ///     The <c>MZ</c> stub says nothing about the architecture - it is a 16-bit DOS program in
        ///     every image ever built - so the machine word is the only statement of it, and it lives
        ///     behind a file offset stored at 0x3C rather than at a fixed place.
        /// </remarks>
        private static NativeBinaryShape PortableExecutable(ReadOnlySpan<byte> payload) {
            if (payload.Length < 0x40)
                return new NativeBinaryShape(NativeBinaryKind.PortableExecutable, "PE (MZ)", "unknown", 0);

            int headerAt = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x3C));
            if (headerAt < 0 || headerAt + 6 > payload.Length ||
                payload[headerAt] != 0x50 || payload[headerAt + 1] != 0x45 ||
                payload[headerAt + 2] != 0 || payload[headerAt + 3] != 0)
                return new NativeBinaryShape(NativeBinaryKind.PortableExecutable, "PE (MZ)", "unknown", 0);

            ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(headerAt + 4));
            (string name, int bits) = machine switch {
                0x014C => ("x86", 32),
                0x8664 => ("x86-64", 64),
                0x0200 => ("Itanium", 64),
                0x01C0 => ("ARM", 32),
                0xAA64 => ("ARM64", 64),
                _ => ("machine 0x" + machine.ToString("X4"), 0)
            };

            return new NativeBinaryShape(NativeBinaryKind.PortableExecutable, "PE (MZ)", name, bits);
        }

        private static NativeBinaryShape Elf(ReadOnlySpan<byte> payload) {
            if (payload.Length < 20)
                return new NativeBinaryShape(NativeBinaryKind.Elf, "ELF", "unknown", 0);

            //EI_CLASS states the word width outright, which is why an ELF needs no machine lookup to
            //answer the 32-versus-64 question the group names raise.
            int bits = payload[4] switch { 1 => 32, 2 => 64, _ => 0 };
            bool littleEndian = payload[5] != 2;

            ushort machine = littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(18))
                : BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(18));

            string name = machine switch {
                3 => "x86",
                62 => "x86-64",
                20 => "PowerPC",
                21 => "PowerPC64",
                40 => "ARM",
                183 => "AArch64",
                _ => "machine " + machine
            };

            return new NativeBinaryShape(NativeBinaryKind.Elf, "ELF", name, bits);
        }

        private static NativeBinaryShape MachO(ReadOnlySpan<byte> payload, int bits, bool littleEndian) {
            string name = "unknown";
            if (payload.Length >= 8) {
                int cpu = littleEndian
                    ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4))
                    : BinaryPrimitives.ReadInt32BigEndian(payload.Slice(4));
                name = CpuName(cpu);
            }

            return new NativeBinaryShape(NativeBinaryKind.MachO, "Mach-O", name, bits);
        }

        /// <summary>
        ///     Names the architectures a fat binary carries rather than picking one.
        /// </summary>
        /// <remarks>
        ///     A universal library has no single word width, and reporting the first slice's would be
        ///     a statement about the file layout dressed up as a statement about the binary. The
        ///     macos/universal groups are exactly this case.
        /// </remarks>
        private static NativeBinaryShape Universal(ReadOnlySpan<byte> payload) {
            if (payload.Length < 8)
                return new NativeBinaryShape(NativeBinaryKind.MachOUniversal, "Mach-O universal", "unknown", 0);

            uint slices = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4));

            //A fat header is always big-endian by definition, and each fat_arch is 20 bytes opening
            //with the cputype. Bounded rather than trusted: the count is a 32-bit field off disk.
            var named = new System.Collections.Generic.List<string>();
            for (uint i = 0; i < slices && i < 32; i++) {
                int at = 8 + (int) i * 20;
                if (at + 4 > payload.Length)
                    break;
                named.Add(CpuName(BinaryPrimitives.ReadInt32BigEndian(payload.Slice(at))));
            }

            string architecture = named.Count == 0
                ? slices + " slice(s)"
                : string.Join(" + ", named);

            return new NativeBinaryShape(NativeBinaryKind.MachOUniversal, "Mach-O universal", architecture, 0);
        }

        private static string CpuName(int cpuType) {
            return cpuType switch {
                7 => "x86",
                0x01000007 => "x86-64",
                18 => "PowerPC",
                0x01000012 => "PowerPC64",
                12 => "ARM",
                0x0100000C => "ARM64",
                _ => "cpu " + cpuType
            };
        }
    }
}
