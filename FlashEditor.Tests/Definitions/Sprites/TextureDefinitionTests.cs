using FlashEditor;
using FlashEditor.Definitions.Sprites;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
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

            // Pass 1: suppressTexture — inverted boolean (true → 0)
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].suppressTexture ? 0 : 1));

            // Pass 2: force64x64
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].force64x64 ? 1 : 0));

            // Pass 3: excludeFromDrawList
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].excludeFromDrawList ? 1 : 0));

            // Passes 4-5 are one byte read 0..255, because every client consumption masks & 0xff.
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)defs[i].colourGain);
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)defs[i].greyBlendWeight);

            // Passes 6-7: signed bytes
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].effectProgram);
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].effectParams);

            // Pass 8: representativeHsl — unsigned short
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteShort(defs[i].representativeHsl);

            // Pass 9-10: signed bytes
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].scrollU);
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].scrollV);

            // Pass 11-12: booleans
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1827 ? 1 : 0));
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].transposePixels ? 1 : 0));

            // Pass 13: signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].mipmap);

            // Pass 14-16: booleans
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].repeatU ? 1 : 0));
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].repeatV ? 1 : 0));
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].halfFloatUpload ? 1 : 0));

            // Pass 17: ubyte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)defs[i].combineMode);

            // Pass 18: full int
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteInteger(defs[i].waterParams);

            // Pass 19: ubyte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)defs[i].alphaMode);

            s.Flip();
            return s.ToArray();
        }

        [Fact]
        public void DecodeColumnar_Reads_Single_Texture()
        {
            var def0 = new TextureDefinition {
                id = 0, suppressTexture = true, force64x64 = false, excludeFromDrawList = true,
                colourGain = 251, greyBlendWeight = 10, effectProgram = 0, effectParams = 3,
                representativeHsl = 42, scrollU = -1, scrollV = 7,
                field1827 = true, transposePixels = false, mipmap = -3,
                repeatU = false, repeatV = true, halfFloatUpload = false,
                combineMode = 200, waterParams = 0xFF00FF, alphaMode = 5
            };
            byte[] data = BuildColumnarFile(new[] { def0 }, 1);

            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(data));

            Assert.Single(TextureManager.Textures);
            var result = TextureManager.Textures[0];
            Assert.True(result.suppressTexture);
            Assert.False(result.force64x64);
            Assert.True(result.excludeFromDrawList);
            // 251 rather than -5: the stored byte is the same 0xFB either way, and this is the
            // reading the client uses, because it masks & 0xff before every use.
            Assert.Equal(251, result.colourGain);
            Assert.Equal(10, result.greyBlendWeight);
            Assert.Equal(42, result.representativeHsl);
            Assert.True(result.field1827);
            Assert.False(result.transposePixels);
            Assert.Equal(200, result.combineMode);
            Assert.Equal(0xFF00FF, result.waterParams);
            Assert.Equal(5, result.alphaMode);
        }

        [Fact]
        public void DecodeColumnar_Handles_Sparse_Slots()
        {
            // 3 slots: 0=exists, 1=null, 2=exists
            var def0 = new TextureDefinition { id = 0, waterParams = 100 };
            var def2 = new TextureDefinition { id = 2, waterParams = 200 };
            var defs = new TextureDefinition[] { def0, null, def2 };

            byte[] data = BuildColumnarFile(defs, 3);

            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(data));

            Assert.Equal(2, TextureManager.Textures.Count);
            Assert.True(TextureManager.Textures.ContainsKey(0));
            Assert.False(TextureManager.Textures.ContainsKey(1));
            Assert.True(TextureManager.Textures.ContainsKey(2));
            Assert.Equal(100, TextureManager.Textures[0].waterParams);
            Assert.Equal(200, TextureManager.Textures[2].waterParams);
        }

        [Fact]
        public void EncodeFromFields_RoundTrips()
        {
            var def0 = new TextureDefinition {
                id = 0, suppressTexture = true, force64x64 = true, excludeFromDrawList = false,
                colourGain = 128, greyBlendWeight = 255, effectProgram = -1, effectParams = 0,
                representativeHsl = 65535, scrollU = 50, scrollV = -50,
                field1827 = false, transposePixels = true, mipmap = 99,
                repeatU = true, repeatV = false, halfFloatUpload = true,
                combineMode = 0, waterParams = unchecked((int)0xDEADBEEF), alphaMode = 255
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
                id = 0, suppressTexture = false, waterParams = 12345
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
            // suppressTexture is true when the byte is 0 (inverted)
            var s = new JagStream();
            s.WriteShort(1);    // count = 1
            s.WriteByte(1);     // exists
            s.WriteByte(0);     // suppressTexture byte = 0 → suppressTexture = true

            // Passes 2-7: six single-byte fields (force64x64..effectParams)
            for (int i = 0; i < 6; i++) s.WriteByte(0);
            // Pass 8: representativeHsl (unsigned short)
            s.WriteShort(0);
            // Passes 9-17: nine single-byte fields (scrollU..combineMode)
            for (int i = 0; i < 9; i++) s.WriteByte(0);
            // Pass 18: waterParams (full int)
            s.WriteInteger(0);
            // Pass 19: alphaMode (unsigned byte)
            s.WriteByte(0);
            s.Flip();

            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(s.ToArray()));

            Assert.True(TextureManager.Textures[0].suppressTexture);
        }

        [Fact]
        public void DecodeColumnar_Multiple_Textures_AllFields()
        {
            var defs = new TextureDefinition[] {
                new TextureDefinition {
                    id = 0, suppressTexture = true, force64x64 = true, excludeFromDrawList = true,
                    colourGain = 1, greyBlendWeight = 2, effectProgram = 3, effectParams = 4,
                    representativeHsl = 1000, scrollU = 5, scrollV = 6,
                    field1827 = true, transposePixels = true, mipmap = 7,
                    repeatU = true, repeatV = true, halfFloatUpload = true,
                    combineMode = 10, waterParams = 0x112233, alphaMode = 20
                },
                new TextureDefinition {
                    id = 1, suppressTexture = false, force64x64 = false, excludeFromDrawList = false,
                    colourGain = 255, greyBlendWeight = 254, effectProgram = -3, effectParams = -4,
                    representativeHsl = 2000, scrollU = -5, scrollV = -6,
                    field1827 = false, transposePixels = false, mipmap = -7,
                    repeatU = false, repeatV = false, halfFloatUpload = false,
                    combineMode = 30, waterParams = 0x445566, alphaMode = 40
                }
            };
            byte[] data = BuildColumnarFile(defs, 2);

            TextureManager.Clear();
            TextureManager.DecodeColumnar(new JagStream(data));

            Assert.Equal(2, TextureManager.Textures.Count);

            var t0 = TextureManager.Textures[0];
            Assert.True(t0.suppressTexture);
            Assert.Equal(1, t0.colourGain);
            Assert.Equal(1000, t0.representativeHsl);
            Assert.Equal(0x112233, t0.waterParams);

            var t1 = TextureManager.Textures[1];
            Assert.False(t1.suppressTexture);
            Assert.Equal(255, t1.colourGain);
            Assert.Equal(2000, t1.representativeHsl);
            Assert.Equal(0x445566, t1.waterParams);

            // Round-trip through the field encoder, which is the one this exercises: the write path
            // would replay the stored bytes and never read a field.
            byte[] reencoded = TextureManager.EncodeFromFields().ToArray();
            Assert.Equal(data, reencoded);
        }
    }
}
