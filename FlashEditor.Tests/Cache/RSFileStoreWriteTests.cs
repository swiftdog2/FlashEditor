using FlashEditor;
using FlashEditor.cache;
using FlashEditor.Utils;
using System;
using System.IO;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    public class RSFileStoreWriteTests
    {
        public RSFileStoreWriteTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
        }

        private static RSFileStore CreateStore(string dir)
        {
            Directory.CreateDirectory(dir);
            const int sectorSize = 520; // RSSector.SIZE
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.dat2"), new byte[sectorSize]);
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx0"), Array.Empty<byte>());
            return new RSFileStore(dir);
        }

        [Fact]
        public void Write_NewAndResizedContainers_RoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                var store = CreateStore(dir);

                var payload = new JagStream(new byte[] {1,2,3,4});
                var container = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                                 RSConstants.NO_COMPRESSION, payload, 1);
                store.Write(0, 0, container.Encode());

                var bigger = new JagStream(new byte[600]);
                for(int i=0;i<bigger.Length;i++) bigger.WriteByte((byte)(i & 0xFF));
                var bigContainer = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                                   RSConstants.NO_COMPRESSION, bigger, 1);
                store.Write(0, 0, bigContainer.Encode());

                var smaller = new JagStream(new byte[]{5});
                var smallContainer = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                                    RSConstants.NO_COMPRESSION, smaller, 1);
                store.Write(0, 0, smallContainer.Encode());
            }
            finally
            {
                if(Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}
