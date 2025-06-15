using FlashEditor.Cache;
using FlashEditor.Cache.CheckSum;

namespace FlashEditor.Tests;

internal static class ChecksumTableExtensions
{
    internal static JagStream EncodeSimple(this ChecksumTable table)
    {
        JagStream stream = new JagStream();
        for (int i = 0; i < table.GetSize(); i++)
        {
            var entry = table.getCheckSumEntry(i);
            stream.WriteInteger(entry.GetCrc());
            stream.WriteInteger(entry.GetVersion());
        }
        return stream.Flip();
    }
}
