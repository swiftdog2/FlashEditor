using System;
using ICSharpCode.SharpZipLib.Checksum;
using FlashEditor.cache;

namespace FlashEditor.Cache.Util
{
    /// <summary>
    /// Utility helpers for calculating CRC-32 checksums and
    /// updating reference table metadata.
    /// </summary>
    public static class CRC32Helper
    {
        /// <summary>
        /// Computes the CRC-32 value of the provided <paramref name="data"/>.
        /// </summary>
        /// <param name="data">Span containing the bytes to checksum.</param>
        /// <returns>The unsigned CRC-32 result.</returns>
        public static uint ComputeCrc32(ReadOnlySpan<byte> data)
        {
            var crc = new Crc32();
            if (!data.IsEmpty)
                crc.Update(data.ToArray());
            return unchecked((uint)crc.Value);
        }

        /// <summary>
        /// Convenience extension for computing a CRC-32 over an entire array.
        /// </summary>
        /// <param name="bytes">The data to hash.</param>
        /// <returns>The CRC-32 value.</returns>
        public static uint Crc32(this byte[] bytes)
            => ComputeCrc32(bytes);

        /// <summary>
        /// Updates the CRC and version for <paramref name="groupId"/> within
        /// <paramref name="table"/> based on the encoded bytes of
        /// <paramref name="container"/>.
        /// </summary>
        /// <param name="container">Container holding the archive data.</param>
        /// <param name="table">Reference table to update.</param>
        /// <param name="groupId">Group/archive id within the table.</param>
        /// <param name="usesXtea">Indicates the archive uses XTEA encryption.</param>
        public static void ApplyCrcAndVersion(
            RSContainer container,
            RSReferenceTable table,
            int groupId,
            bool usesXtea)
        {
            // Obtain the encoded container bytes and exclude the version field
            var encoded = container.Encode();
            int lenWithoutVersion = (int)encoded.Length;
            if (container.GetVersion() != -1)
                lenWithoutVersion -= 2;

            uint crc = ComputeCrc32(encoded.ToArray().AsSpan(0, lenWithoutVersion));

            var entry = table.GetEntry(groupId);
            if (entry != null)
            {
                entry.SetCrc((int)crc);
                entry.SetVersion(entry.GetVersion() + 1);
            }

            // The current reference table implementation does not expose
            // per-group flags or a dirty marker. Those aspects are therefore
            // not handled here.
            _ = usesXtea; // parameter acknowledged to avoid warnings
        }
    }
}

/* Example usage:
var table = new RSReferenceTable { format = 6, version = 1 };
var container = new RSContainer();
CRC32Helper.ApplyCrcAndVersion(container, table, 0, false);
*/