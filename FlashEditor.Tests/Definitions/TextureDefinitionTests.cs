using FlashEditor;
using System.Collections.Generic;
using FlashEditor.Definitions.Sprites;
using FlashEditor.cache;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    public class TextureDefinitionTests
    {
        private static byte[] BuildTexture(int count, ushort[] fileIds, int[] colors)
        {
            var s = new JagStream();
            s.WriteShort(0);
            s.WriteByte(0);
            s.WriteByte((byte)count);
            foreach (var id in fileIds)
                s.WriteShort((short)id);
            if (count > 1)
            {
                for (int i = 0; i < count - 1; i++) s.WriteByte(0);
                for (int i = 0; i < count - 1; i++) s.WriteByte(0);
            }
            foreach (var c in colors)
                s.WriteInteger(c);
            s.WriteByte(0);
            s.WriteByte(0);
            s.Flip();
            return s.ToArray();
        }

        [Fact]
        public void Loader_Decodes_All_Entries()
        {
            var loader = new TextureLoader();

            var archive = new RSArchive();
            archive.PutEntry(0, new JagStream(BuildTexture(1, new ushort[] { 10 }, new int[] { 0x11223344 })));
            archive.PutEntry(1, new JagStream(BuildTexture(2, new ushort[] { 20, 21 }, new int[] { 1, 2 })));

            var defs = new List<TextureDefinition>();
            foreach (var kvp in archive.entries)
                defs.Add(loader.Load(kvp.Key, kvp.Value.ToArray()));

            Assert.Equal(2, defs.Count);
            Assert.Equal(1, defs[0].fileIds.Length);
            Assert.Equal(2, defs[1].fileIds.Length);
        }
    }
}
