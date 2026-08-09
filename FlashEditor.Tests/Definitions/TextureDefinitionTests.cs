using FlashEditor;
using FlashEditor.Definitions.Sprites;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Columnar (index 26) texture metadata codec tests.
    /// </summary>
    /// <remarks>
    ///     In the "RealCache" collection despite needing no cache: every test here calls
    ///     <c>TextureManager.Clear</c>, which disposes every definition in a static dictionary that
    ///     <c>TextureGraphConformanceTests</c> and <c>RealCacheMapIconTests</c> are reading. Sharing
    ///     one collection is what stops xunit running them at the same time.
    /// </remarks>
    [Collection("RealCache")]
    public class TextureDefinitionTests
    {
        /// <summary>
        /// Builds a minimal columnar texture file with the given definitions.
        /// Matches the Class260 constructor format from the Hydra client.
        /// </summary>
        private static byte[] BuildColumnarFile(TextureDefinition[] defs, int count)
        {
            var s = new JagStream();
            s.WriteShort(count);

            // Pass 0: existence flags
            for (int i = 0; i < count; i++)
                s.WriteByte((byte)(defs[i] != null ? 1 : 0));

            // Pass 1: field1825 — inverted boolean (true → 0)
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1825 ? 0 : 1));

            // Pass 2: field1822
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1822 ? 1 : 0));

            // Pass 3: field1833
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1833 ? 1 : 0));

            // Pass 4-7: signed bytes
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1829);
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1830);
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1820);
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1816);

            // Pass 8: field1831 — unsigned short
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteShort(defs[i].field1831);

            // Pass 9-10: signed bytes
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1823);
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1837);

            // Pass 11-12: booleans
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1827 ? 1 : 0));
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1824 ? 1 : 0));

            // Pass 13: signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1832);

            // Pass 14-16: booleans
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1826 ? 1 : 0));
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1819 ? 1 : 0));
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1817 ? 1 : 0));

            // Pass 17: ubyte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)defs[i].field1821);

            // Pass 18: full int
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteInteger(defs[i].field1835);

            // Pass 19: ubyte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)defs[i].field1818);

            s.Flip();
            return s.ToArray();
        }

        [Fact]
        public void DecodeColumnar_Reads_Single_Texture()
        {
            var def0 = new TextureDefinition {
                id = 0, field1825 = true, field1822 = false, field1833 = true,
                field1829 = -5, field1830 = 10, field1820 = 0, field1816 = 3,
                field1831 = 42, field1823 = -1, field1837 = 7,
                field1827 = true, field1824 = false, field1832 = -3,
                field1826 = false, field1819 = true, field1817 = false,
                field1821 = 200, field1835 = 0xFF00FF, field1818 = 5
            };
            byte[] data = BuildColumnarFile(new[] { def0 }, 1);

            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(data));

            Assert.Single(TextureManager.Textures);
            var result = TextureManager.Textures[0];
            Assert.True(result.field1825);
            Assert.False(result.field1822);
            Assert.True(result.field1833);
            Assert.Equal(-5, result.field1829);
            Assert.Equal(10, result.field1830);
            Assert.Equal(42, result.field1831);
            Assert.True(result.field1827);
            Assert.False(result.field1824);
            Assert.Equal(200, result.field1821);
            Assert.Equal(0xFF00FF, result.field1835);
            Assert.Equal(5, result.field1818);
        }

        [Fact]
        public void DecodeColumnar_Handles_Sparse_Slots()
        {
            // 3 slots: 0=exists, 1=null, 2=exists
            var def0 = new TextureDefinition { id = 0, field1835 = 100 };
            var def2 = new TextureDefinition { id = 2, field1835 = 200 };
            var defs = new TextureDefinition[] { def0, null, def2 };

            byte[] data = BuildColumnarFile(defs, 3);

            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(data));

            Assert.Equal(2, TextureManager.Textures.Count);
            Assert.True(TextureManager.Textures.ContainsKey(0));
            Assert.False(TextureManager.Textures.ContainsKey(1));
            Assert.True(TextureManager.Textures.ContainsKey(2));
            Assert.Equal(100, TextureManager.Textures[0].field1835);
            Assert.Equal(200, TextureManager.Textures[2].field1835);
        }

        [Fact]
        public void EncodeFromFields_RoundTrips()
        {
            var def0 = new TextureDefinition {
                id = 0, field1825 = true, field1822 = true, field1833 = false,
                field1829 = -128, field1830 = 127, field1820 = -1, field1816 = 0,
                field1831 = 65535, field1823 = 50, field1837 = -50,
                field1827 = false, field1824 = true, field1832 = 99,
                field1826 = true, field1819 = false, field1817 = true,
                field1821 = 0, field1835 = unchecked((int)0xDEADBEEF), field1818 = 255
            };
            byte[] original = BuildColumnarFile(new[] { def0 }, 1);

            // Decode
            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(original));

            // The field encoder specifically, not the write path: EncodeColumnar now replays the
            // stored bytes of every column nobody edited, so it would pass here without the fields
            // being consulted at all.
            byte[] reencoded = TextureManager.EncodeFromFields().ToArray();

            Assert.Equal(original, reencoded);
        }

        [Fact]
        public void EncodeColumnar_RawData_RoundTrips()
        {
            var def0 = new TextureDefinition {
                id = 0, field1825 = false, field1835 = 12345
            };
            byte[] original = BuildColumnarFile(new[] { def0 }, 1);

            // Decode (stores raw data)
            TextureManager.Clear();
            TextureManager.RawIndexData = original;
            TextureManager.DecodeColumnar(new JagStream(original));

            // Encode — should use raw data path
            byte[] reencoded = TextureManager.EncodeColumnar().ToArray();
            Assert.Equal(original, reencoded);
        }

        [Fact]
        public void DecodeColumnar_Field1825_InvertedBoolean()
        {
            // field1825 is true when the byte is 0 (inverted)
            var s = new JagStream();
            s.WriteShort(1);    // count = 1
            s.WriteByte(1);     // exists
            s.WriteByte(0);     // field1825 byte = 0 → field1825 = true

            // Passes 2-7: six single-byte fields (field1822..field1816)
            for (int i = 0; i < 6; i++) s.WriteByte(0);
            // Pass 8: field1831 (unsigned short)
            s.WriteShort(0);
            // Passes 9-17: nine single-byte fields (field1823..field1821)
            for (int i = 0; i < 9; i++) s.WriteByte(0);
            // Pass 18: field1835 (full int)
            s.WriteInteger(0);
            // Pass 19: field1818 (unsigned byte)
            s.WriteByte(0);
            s.Flip();

            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(s.ToArray()));

            Assert.True(TextureManager.Textures[0].field1825);
        }

        [Fact]
        public void DecodeColumnar_Multiple_Textures_AllFields()
        {
            var defs = new TextureDefinition[] {
                new TextureDefinition {
                    id = 0, field1825 = true, field1822 = true, field1833 = true,
                    field1829 = 1, field1830 = 2, field1820 = 3, field1816 = 4,
                    field1831 = 1000, field1823 = 5, field1837 = 6,
                    field1827 = true, field1824 = true, field1832 = 7,
                    field1826 = true, field1819 = true, field1817 = true,
                    field1821 = 10, field1835 = 0x112233, field1818 = 20
                },
                new TextureDefinition {
                    id = 1, field1825 = false, field1822 = false, field1833 = false,
                    field1829 = -1, field1830 = -2, field1820 = -3, field1816 = -4,
                    field1831 = 2000, field1823 = -5, field1837 = -6,
                    field1827 = false, field1824 = false, field1832 = -7,
                    field1826 = false, field1819 = false, field1817 = false,
                    field1821 = 30, field1835 = 0x445566, field1818 = 40
                }
            };
            byte[] data = BuildColumnarFile(defs, 2);

            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(data));

            Assert.Equal(2, TextureManager.Textures.Count);

            var t0 = TextureManager.Textures[0];
            Assert.True(t0.field1825);
            Assert.Equal(1, t0.field1829);
            Assert.Equal(1000, t0.field1831);
            Assert.Equal(0x112233, t0.field1835);

            var t1 = TextureManager.Textures[1];
            Assert.False(t1.field1825);
            Assert.Equal(-1, t1.field1829);
            Assert.Equal(2000, t1.field1831);
            Assert.Equal(0x445566, t1.field1835);

            // Round-trip through the field encoder, which is the one this exercises: the write path
            // would replay the stored bytes and never read a field.
            byte[] reencoded = TextureManager.EncodeFromFields().ToArray();
            Assert.Equal(data, reencoded);
        }
    }
}
