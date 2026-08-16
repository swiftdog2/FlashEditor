using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Natives;
using Xunit;

namespace FlashEditor.Tests.Definitions.Natives
{
    /// <summary>
    ///     The name split, the header classifier and the anomaly rule, against bytes stated here.
    /// </summary>
    /// <remarks>
    ///     Synthetic, because the anomaly rule has to be checked on name sets this cache does not
    ///     contain - two spellings used equally often, and a cache with no disagreement at all - and
    ///     because a PE header can be stated in twenty bytes. The shipped index is swept separately
    ///     by <c>RealCacheNativeLibraryTests</c>.
    /// </remarks>
    public class NativeLibraryClassificationTests
    {
        [Theory]
        [InlineData("windows/x86/jaggl.dll", "windows", "x86", "jaggl", ".dll")]
        [InlineData("windows/x86_64/sw3d.dll", "windows", "x86_64", "sw3d", ".dll")]
        [InlineData("linux/x86_64/libjaclib.so", "linux", "x86_64", "jaclib", ".so")]
        [InlineData("macos/universal/libjaggl.dylib", "macos", "universal", "jaggl", ".dylib")]
        [InlineData("windows/msjava/jagmisc.dll", "windows", "msjava", "jagmisc", ".dll")]
        public void ANameSplitsIntoTheThreeThingsItStates(string path, string os, string arch,
            string library, string extension)
        {
            NativeLibraryName name = NativeLibraryName.Parse(path);

            Assert.True(name.IsWellFormed);
            Assert.Equal(os, name.OperatingSystem);
            //As stored, never normalised: x64 and x86_64 mean the same machine and the fact that
            //this cache uses both is the finding.
            Assert.Equal(arch, name.Architecture);
            //The lib prefix is a platform convention rather than part of the family, so libjaggl.so
            //and jaggl.dll have to group together.
            Assert.Equal(library, name.Library);
            Assert.Equal(extension, name.Extension);
        }

        [Fact]
        public void AnUnrecoveredNameIsNotPretendedToBeStructured()
        {
            NativeLibraryName name = NativeLibraryName.Parse(null);

            Assert.False(name.IsWellFormed);
            Assert.Equal(string.Empty, name.Path);
            Assert.Equal(string.Empty, name.Library);
        }

        /// <summary>
        ///     A PE image states its architecture in a word behind an offset, not in its magic.
        /// </summary>
        /// <remarks>
        ///     The <c>MZ</c> stub is a 16-bit DOS program in every image ever built, so it says
        ///     nothing at all about the machine. That is why the word width shown beside a name is
        ///     read from the COFF header rather than from the <c>x86</c> or <c>x86_64</c> in the path.
        /// </remarks>
        [Theory]
        [InlineData(0x014C, "x86", 32)]
        [InlineData(0x8664, "x86-64", 64)]
        public void APeImageReportsTheMachineItsCoffHeaderNames(int machine, string expected, int bits)
        {
            byte[] image = new byte[0x60];
            image[0] = 0x4D;
            image[1] = 0x5A;
            image[0x3C] = 0x40;
            image[0x40] = 0x50;
            image[0x41] = 0x45;
            image[0x44] = (byte) (machine & 0xFF);
            image[0x45] = (byte) ((machine >> 8) & 0xFF);

            NativeBinaryShape shape = NativeBinaryShape.Of(image);

            Assert.Equal(NativeBinaryKind.PortableExecutable, shape.Kind);
            Assert.Equal(expected, shape.Architecture);
            Assert.Equal(bits, shape.Bits);
        }

        [Theory]
        [InlineData(1, 3, "x86", 32)]
        [InlineData(2, 62, "x86-64", 64)]
        public void AnElfReportsTheClassAndMachineItsHeaderNames(byte elfClass, int machine,
            string expected, int bits)
        {
            byte[] image = new byte[64];
            image[0] = 0x7F;
            image[1] = 0x45;
            image[2] = 0x4C;
            image[3] = 0x46;
            image[4] = elfClass;
            image[5] = 1;
            image[18] = (byte) (machine & 0xFF);
            image[19] = (byte) ((machine >> 8) & 0xFF);

            NativeBinaryShape shape = NativeBinaryShape.Of(image);

            Assert.Equal(NativeBinaryKind.Elf, shape.Kind);
            Assert.Equal(expected, shape.Architecture);
            Assert.Equal(bits, shape.Bits);
        }

        [Fact]
        public void AThinMachOReportsItsOwnWordWidth()
        {
            //FE ED FA CF big-endian, cputype 0x01000007, which is a 64-bit x86 image.
            byte[] image = { 0xFE, 0xED, 0xFA, 0xCF, 0x01, 0x00, 0x00, 0x07 };

            NativeBinaryShape shape = NativeBinaryShape.Of(image);

            Assert.Equal(NativeBinaryKind.MachO, shape.Kind);
            Assert.Equal("x86-64", shape.Architecture);
            Assert.Equal(64, shape.Bits);
        }

        /// <summary>
        ///     A universal binary has no single word width, and one is not invented for it.
        /// </summary>
        /// <remarks>
        ///     Reporting the first slice's width would be a statement about the file layout dressed
        ///     as a statement about the binary, and it is the macos/universal groups that would carry
        ///     it.
        /// </remarks>
        [Fact]
        public void AUniversalMachONamesItsSlicesRatherThanPickingOne()
        {
            byte[] image = new byte[8 + 40];
            image[0] = 0xCA;
            image[1] = 0xFE;
            image[2] = 0xBA;
            image[3] = 0xBE;
            image[7] = 2;
            image[11] = 7;              //slice 0, cputype 7 - x86
            image[28] = 18;             //slice 1, cputype 18 - PowerPC

            NativeBinaryShape shape = NativeBinaryShape.Of(image);

            Assert.Equal(NativeBinaryKind.MachOUniversal, shape.Kind);
            Assert.Equal("x86 + PowerPC", shape.Architecture);
            Assert.Equal(0, shape.Bits);
            Assert.Equal(string.Empty, shape.BitsText);
        }

        /// <summary>
        ///     Every committed name hashes to a distinct identifier and resolves back to itself.
        /// </summary>
        /// <remarks>
        ///     What makes the name table a join rather than a guess. Two names colliding on one hash
        ///     would silently make one of them unreachable, and the table is the only thing standing
        ///     between this index and a column of signed integers.
        /// </remarks>
        [Fact]
        public void EveryCommittedNameHashesToADistinctIdentifier()
        {
            IReadOnlyList<string> names = NativeLibraryNames.KnownNames;

            var hashes = new HashSet<int>();
            foreach (string name in names)
            {
                int hash = NameHasher.GetNameHash(name);
                Assert.True(hashes.Add(hash), "two committed names collide on hash " + hash + ": " + name);
                Assert.True(NativeLibraryNames.TryGetName(hash, out string recovered));
                Assert.Equal(name, recovered);
            }

            Assert.Equal(names.Count, hashes.Count);
        }

        /// <summary>
        ///     The client's own path rule would not have produced this cache's name set.
        /// </summary>
        /// <remarks>
        ///     The reason the candidate list is committed rather than generated.
        ///     <c>Class365.java:70-72</c> only ever emits <c>x86_64/</c> for a 64-bit host, so a
        ///     generated list would carry <c>windows/x86_64/jagmisc.dll</c>, which hashes to nothing
        ///     in this cache, and would not carry <c>windows/x64/jagmisc.dll</c>, which is what is
        ///     actually stored.
        /// </remarks>
        [Fact]
        public void TheCommittedNamesCarryTheSpellingTheCacheUsesAndNotTheOneTheClientAsksFor()
        {
            Assert.Contains("windows/x64/jagmisc.dll", NativeLibraryNames.KnownNames);
            Assert.DoesNotContain("windows/x86_64/jagmisc.dll", NativeLibraryNames.KnownNames);
        }

        /// <summary>
        ///     The minority spelling is reported and the majority is not.
        /// </summary>
        /// <remarks>
        ///     Derived from the name set rather than hardcoded to a group id, so it keeps working on
        ///     a cache that renumbers its groups and would find a second disagreement if one existed.
        /// </remarks>
        [Fact]
        public void AnOperatingSystemSpellingOneArchitectureTwoWaysReportsTheMinoritySpelling()
        {
            var names = new Dictionary<int, NativeLibraryName> {
                { 7, NativeLibraryName.Parse("windows/x86_64/hw3d.dll") },
                { 8, NativeLibraryName.Parse("windows/x86_64/jaggl.dll") },
                { 9, NativeLibraryName.Parse("windows/x86_64/jaclib.dll") },
                { 11, NativeLibraryName.Parse("windows/x64/jagmisc.dll") },
                { 2, NativeLibraryName.Parse("windows/x86/jaggl.dll") }
            };

            NativeLibraryCensus census = NativeLibraryCensus.From(names, names.Count);

            Assert.Equal(new[] { 11 }, census.AnomalousGroups.OrderBy(id => id).ToArray());
            Assert.Contains("x86_64", census.AnomalyFor(11));
            Assert.Null(census.AnomalyFor(7));
            //The 32-bit family uses one spelling, so it is not dragged in by the 64-bit one.
            Assert.Null(census.AnomalyFor(2));
        }

        [Fact]
        public void AConsistentNameSetReportsNoAnomaly()
        {
            var names = new Dictionary<int, NativeLibraryName> {
                { 1, NativeLibraryName.Parse("windows/x86/hw3d.dll") },
                { 7, NativeLibraryName.Parse("windows/x86_64/hw3d.dll") },
                { 26, NativeLibraryName.Parse("linux/x86/libhw3d.so") }
            };

            Assert.Empty(NativeLibraryCensus.From(names, names.Count).AnomalousGroups);
        }

        /// <summary>
        ///     Two spellings used equally often report neither.
        /// </summary>
        /// <remarks>
        ///     There would be no evidence for calling either one the odd one out, and guessing is
        ///     what this index punishes. The rule refusing here is what makes it a measurement rather
        ///     than a preference for a spelling.
        /// </remarks>
        [Fact]
        public void TwoSpellingsUsedEquallyOftenReportNeither()
        {
            var names = new Dictionary<int, NativeLibraryName> {
                { 1, NativeLibraryName.Parse("windows/x64/a.dll") },
                { 2, NativeLibraryName.Parse("windows/x86_64/b.dll") }
            };

            Assert.Empty(NativeLibraryCensus.From(names, names.Count).AnomalousGroups);
        }
    }
}
