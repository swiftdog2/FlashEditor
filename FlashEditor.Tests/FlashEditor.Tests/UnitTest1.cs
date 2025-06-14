using System;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using FlashEditor.cache;
using FlashEditor.Cache;
using FlashEditor.Cache.CheckSum;
using FlashEditor.Cache.Util;
using Xunit;
using FlashEditor.Cache.Util.Crypto;

namespace FlashEditor.Tests;

public class CodecTests
{
    [Fact]
    public void RSSector_RoundTrip()
    {
        byte[] data = Enumerable.Range(0, RSSector.DATA_LEN).Select(i => (byte)i).ToArray();
        var sector = new RSSector(1, 42, 3, 99, data);
        JagStream enc = sector.Encode();
        var dec = RSSector.Decode(new JagStream(enc.ToArray()));
        Assert.Equal(sector.GetType(), dec.GetType());
        Assert.Equal(sector.GetId(), dec.GetId());
        Assert.Equal(sector.GetChunk(), dec.GetChunk());
        Assert.Equal(sector.GetNextSector(), dec.GetNextSector());
        Assert.Equal(sector.GetData(), dec.GetData());
    }

    [Fact]
    public void RSContainer_RoundTrip()
    {
        var content = new JagStream(Encoding.ASCII.GetBytes("hello world"));
        var c = new RSContainer(0, 1, (byte)RSConstants.NO_COMPRESSION, content, 1);
        JagStream encoded = c.Encode();
        var decoded = RSContainer.Decode(encoded);
        Assert.Equal(c.GetCompressionType(), decoded.GetCompressionType());
        Assert.Equal(content.ToArray(), decoded.GetStream().ToArray());
        Assert.Equal(c.GetVersion(), decoded.GetVersion());
    }

    [Fact]
    public void RSArchive_RoundTrip()
    {
        var a = new RSArchive();
        a.PutEntry(0, new JagStream(new byte[]{1,2,3}));
        a.PutEntry(1, new JagStream(new byte[]{4,5,6,7}));
        JagStream encoded = a.Encode();
        var decoded = RSArchive.Decode(encoded, a.entries.Count);
        Assert.Equal(a.entries.Count, decoded.entries.Count);
        foreach (var kv in a.entries)
            Assert.Equal(kv.Value.ToArray(), decoded.entries[kv.Key].ToArray());
    }

    [Fact]
    public void RSReferenceTable_RoundTrip()
    {
        var table = new RSReferenceTable
        {
            format = 6,
            version = 1,
            flags = 0,
            hasIdentifiers = false,
            usesWhirlpool = false,
            entryHashes = false,
            sizes = false,
            validArchivesCount = 1,
            validArchiveIds = new []{0},
            type = 0
        };
        var entry = new RSEntry(0);
        entry.SetVersion(1);
        entry.SetChildEntries(new System.Collections.Generic.SortedDictionary<int, RSChildEntry>());
        entry.SetValidFileIds(Array.Empty<int>());
        table.PutEntry(0, entry);
        JagStream encoded = table.Encode();
        var decoded = RSReferenceTable.Decode(new JagStream(encoded.ToArray()));
        JagStream encoded2 = decoded.Encode();
        Assert.Equal(encoded.ToArray(), encoded2.ToArray());
    }

    [Fact]
    public void ChecksumTable_RoundTrip()
    {
        var t = new ChecksumTable(2);
        t.setCheckSumEntry(0, new CheckSumEntry(123,1,0,0,new byte[64]));
        t.setCheckSumEntry(1, new CheckSumEntry(456,2,0,0,new byte[64]));
        JagStream encoded = t.EncodeSimple();
        var decoded = ChecksumTable.Decode(new JagStream(encoded.ToArray()));
        JagStream encoded2 = decoded.EncodeSimple();
        Assert.Equal(encoded.ToArray(), encoded2.ToArray());
    }
}

public class CryptoTests
{
    [Fact]
    public void Djb2_Hash()
    {
        int h = Djb2.Hash("test");
        Assert.Equal(3556498, h);
    }

    [Fact]
    public void CRC32_Test()
    {
        var crc = new CRC32Generator();
        byte[] data = Encoding.ASCII.GetBytes("hello");
        crc.Update(data,0,data.Length);
        Assert.Equal(907060870, crc.Value);
    }

    [Fact]
    public void Whirlpool_Sha512()
    {
        byte[] data = Encoding.ASCII.GetBytes("hello");
        byte[] expected;
        using(var sha = SHA512.Create()) expected = sha.ComputeHash(data);
        byte[] actual = Whirlpool.GetHash(data);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void JagStream_ReadWrite()
    {
        var s = new JagStream();
        s.WriteByte(0x12);
        s.WriteShort(0x3456);
        s.WriteInteger(0x789ABCDE);
        s.Flip();
        Assert.Equal(0x12, s.ReadUnsignedByte());
        Assert.Equal(0x3456, s.ReadUnsignedShort());
        Assert.Equal(0x789ABCDE, s.ReadInt());
    }
}
