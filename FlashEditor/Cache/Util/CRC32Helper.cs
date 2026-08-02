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
        /// <remarks>
        /// The CRC a reference table carries covers the STORED bytes, so for an encrypted
        /// archive it has to be taken over the ciphertext. Encoding without the key both
        /// checksums the wrong bytes and, since the container is encoded here anyway,
        /// invites the caller to write that plaintext out - which destroys the archive.
        /// The key therefore travels with the container rather than being described by a
        /// bool that can disagree with it.
        /// </remarks>
        /// <param name="container">Container holding the archive data.</param>
        /// <param name="table">Reference table to update.</param>
        /// <param name="groupId">Group/archive id within the table.</param>
        /// <param name="xteaKey">
        /// The key the archive is stored under, or null when it is stored in the clear.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// The container was decoded from encrypted bytes but no key was supplied.
        /// </exception>
        public static void ApplyCrcAndVersion(
            RSContainer container,
            RSReferenceTable table,
            int groupId,
            int[] xteaKey)
        {
            if (container.StoredEncrypted && xteaKey == null)
                throw new InvalidOperationException(
                    "Group " + groupId + " was decoded from encrypted bytes, so its CRC cannot be" +
                    " computed without the XTEA key it is stored under.");

            // Obtain the encoded container bytes and exclude the version field
            var encoded = container.Encode(xteaKey);
            int lenWithoutVersion = (int)encoded.Length;
            if (container.GetVersion() != -1)
                lenWithoutVersion -= 2;

            uint crc = ComputeCrc32(encoded.ToArray().AsSpan(0, lenWithoutVersion));

            var entry = table.GetArchiveEntry(groupId);
            if (entry != null)
            {
                entry.SetCrc((int)crc);
                entry.SetVersion(entry.GetVersion() + 1);
                entry.UsesXtea = xteaKey != null;
            }

            // bump reference table version to mark it dirty
            table.version++;
        }
    }
}

/* Example usage:
var table = new RSReferenceTable { format = 6, version = 1 };
var container = new RSContainer();
CRC32Helper.ApplyCrcAndVersion(container, table, 0, null);
*/