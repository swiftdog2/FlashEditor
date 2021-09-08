﻿using FlashEditor.utils;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Tests {
    class CompressionTests {
        public static void Main() {
            byte[] bytes = {31,139,8,0,224,106,28,97,0,255,237,212,105,84,85,85,24,198,241,39,83,180,80,52,69,16,145,68,19,28,112,72,64,68,10,73,197,33,173,28,82,172,212,202,156,18,1,115,40,135,114,22,135,82,4,181,132,28,83,75,195,89,8,203,156,48,156,82,83,193,41,28,200,33,173,8,17,17,43,68,106,211,218,31,252,216,7,215,138,181,252,223,187,158,117,206,189,231,61,251,238,253,254,206,190,14,82,153,100,169,155,164,71,74,245,219,188,14,251,182,61,221,57,230,234,238,85,1,237,206,76,186,188,233,144,255,140,198,137,123,7,101,116,235,183,173,202,109,247,180,176,122,119,79,59,228,78,27,154,223,97,67,124,84,230,249,102,67,188,124,50,243,151,4,55,168,176,171,85,252,124,255,213,19,247,119,26,29,214,63,222,47,32,124,254,136,156,190,231,114,11,162,50,154,20,207,202,222,232,152,185,114,129,71,214,132,64,119,223,97,99,46,93,184,150,122,108,221,140,180,125,77,60,39,111,140,91,20,42,255,246,69,151,79,84,12,142,109,182,127,243,112,191,188,230,93,19,206,221,91,27,176,254,192,192,11,9,23,35,66,138,66,167,212,138,157,238,20,121,114,79,211,22,11,93,55,249,165,239,116,73,175,191,189,187,219,241,232,232,194,136,144,142,65,215,93,215,102,85,140,242,72,244,30,181,52,36,122,82,242,202,194,133,89,119,183,236,74,153,89,249,192,149,164,148,29,149,61,102,143,119,75,60,155,30,214,59,161,224,96,143,192,53,203,54,228,248,14,118,140,220,217,58,117,241,184,158,65,94,203,195,11,123,186,196,29,157,83,195,179,79,191,152,107,23,143,212,46,10,31,89,181,69,203,54,217,254,83,187,212,188,225,157,22,91,233,248,196,164,136,245,62,62,197,129,13,183,230,229,150,119,158,119,170,206,149,1,5,13,183,175,112,190,149,147,213,103,224,204,49,55,157,150,118,158,155,60,214,61,163,215,170,59,25,145,166,125,53,76,156,77,234,152,212,180,241,176,159,93,77,30,55,121,202,196,205,196,221,94,115,177,169,107,82,223,196,203,164,150,173,119,180,117,213,236,119,37,247,121,154,52,183,245,46,246,122,37,147,170,246,232,104,231,81,211,158,187,218,251,171,218,90,239,251,198,175,110,143,110,118,172,202,246,188,100,94,45,237,124,106,219,241,74,210,200,36,216,142,211,208,164,189,29,183,156,77,89,147,71,109,28,238,251,190,156,173,43,153,123,61,91,87,214,254,102,125,219,31,191,255,225,129,47,254,207,149,127,235,175,127,247,111,25,179,182,178,102,61,14,42,175,10,122,204,136,58,170,162,233,187,147,233,93,21,61,97,86,89,205,60,1,213,205,202,92,77,199,220,140,130,187,233,182,135,158,52,125,244,52,235,172,107,252,235,25,99,111,179,238,6,166,135,141,228,163,198,106,162,166,106,166,167,141,171,175,252,228,175,22,10,48,253,15,84,43,5,233,25,61,107,122,222,90,33,122,78,109,212,86,237,20,106,250,222,65,29,213,73,207,171,179,186,232,5,189,168,151,212,213,252,195,116,87,15,189,172,158,234,165,48,245,214,43,122,85,175,169,143,250,170,159,94,215,27,122,83,253,245,150,6,104,160,6,105,176,134,232,109,13,85,184,134,41,66,145,138,210,112,189,163,17,26,169,81,26,173,119,245,158,198,104,172,198,105,188,222,215,7,154,160,137,154,164,201,154,162,169,154,166,233,138,214,12,205,212,44,205,214,135,250,72,115,52,87,49,154,167,88,197,105,190,22,104,161,62,214,39,90,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0};
            DebugUtil.Debug("Original length: " + bytes.Length);

            MemoryStream deflateStream = new MemoryStream();
            using(GZipOutputStream gz = new GZipOutputStream(deflateStream)) {
                using(MemoryStream js = new MemoryStream(bytes)) {
                    StreamUtils.Copy(js, gz, new byte[4096]);
                }
            }

            byte[] compressed = deflateStream.ToArray();
            DebugUtil.Debug("Compressed length: " + compressed.Length);
            DebugUtil.PrintByteArray(compressed);

            MemoryStream inflateStream = new MemoryStream();
            using(GZipInputStream gz = new GZipInputStream(new JagStream(compressed)))
                StreamUtils.Copy(gz, inflateStream, new byte[4096]);

            byte[] decompressed = inflateStream.ToArray();
            DebugUtil.Debug("Decompressed length: " + decompressed.Length);
            DebugUtil.PrintByteArray(decompressed);

            /*
            using(JagStream inflateStream = new JagStream()) {
                using(JagStream inputStream = new JagStream(inBytes)) {
                    GZip.Decompress(inputStream, inflateStream, false);
                }

                return inflateStream.ToArray();
            }*/
        }
    }
}