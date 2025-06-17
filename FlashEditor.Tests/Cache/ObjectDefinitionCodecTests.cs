using FlashEditor.Definitions;
using FlashEditor;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    public class ObjectDefinitionCodecTests
    {
        [Fact]
        public void ObjectDefinition_EncodeDecode_RoundTrips()
        {
            var s = new JagStream();
            s.WriteByte(2); s.WriteJagexString("Tree");
            s.WriteByte(14); s.WriteByte(3);
            s.WriteByte(15);
            s.WriteByte(22);
            s.WriteByte(28); s.WriteByte(1);
            s.WriteByte(29); s.WriteSignedByte(-2);
            s.WriteByte(30); s.WriteJagexString("Open");
            s.WriteByte(40); s.WriteByte(1); s.WriteShort(1); s.WriteShort(2);
            s.WriteByte(41); s.WriteByte(1); s.WriteShort(3); s.WriteShort(4);
            s.WriteByte(77); s.WriteShort(0xFFFF); s.WriteShort(0xFFFF); s.WriteByte(0);
            s.WriteByte(78); s.WriteShort(1000); s.WriteByte(1);
            s.WriteByte(150); s.WriteJagexString("Examine");
            s.WriteByte(249); s.WriteByte(1); s.WriteByte(1); s.WriteMedium(0x010203); s.WriteJagexString("hello");
            s.WriteByte(0);
            s.Flip();

            ObjectDefinition def = ObjectDefinition.DecodeFromStream(s);
            JagStream encoded = def.Encode();

            Assert.Equal(s.ToArray(), encoded.ToArray());
        }
    }
}
