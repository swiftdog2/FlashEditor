using Xunit;
using FlashEditor.Cache.Util.Crypto;
using System.Numerics;
using FlashEditor;

public class CryptoTests
{
    [Fact]
    public void Djb2ProducesExpectedHash()
    {
        int hash = Djb2.Hash("test");
        Assert.Equal(3556498, hash);
    }

    [Fact]
    public void XteaRoundTrip()
    {
        var stream = new JagStream();
        stream.WriteInteger(0x12345678);
        int[] key = {1,2,3,4};
        XTEA.Encipher(stream, 0, (int)stream.Length, key);
        XTEA.Decipher(stream, 0, (int)stream.Length, key);
        stream.Seek0();
        Assert.Equal(0x12345678, stream.ReadInt());
    }

    [Fact]
    public void CRC32MatchesZlib()
    {
        byte[] data = System.Text.Encoding.ASCII.GetBytes("hello world");
        int crc = FlashEditor.Cache.Util.CRC32Generator.GetHash(data);
        Assert.Equal(222957957, crc);
    }

    [Fact]
    public void JagStreamReadWriteInt()
    {
        var s = new JagStream();
        s.WriteInteger(unchecked((int)0xCAFEBABE));
        s.Seek0();
        Assert.Equal(unchecked((int)0xCAFEBABE), s.ReadInt());
    }
}
