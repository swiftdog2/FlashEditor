using System;
using System.IO;
using FlashEditor.Definitions.LoadingSprites;

namespace FlashEditor.Tools.LoadingSpriteJvmCheck {
    /// <summary>
    ///     Dumps what this editor draws for each index-32 JPEG, in the format
    ///     <c>ClientImagePath.java</c> writes, so the two readings can be compared pixel for pixel.
    /// </summary>
    /// <remarks>
    ///     The comparison is the only evidence that settles the colour model, so this side has to go
    ///     through the production path and nothing else: <see cref="JagexJpeg.Decode"/>,
    ///     <see cref="BaselineJpegDecoder.Decode"/> and <see cref="JpegRaster.ToArgb"/>, exactly as
    ///     the Loading Sprites tab does. Reimplementing the transform here would compare a fresh
    ///     guess against the JVM rather than comparing what the editor actually shows.
    /// </remarks>
    internal static class Program {
        /// <summary>
        ///     Writes one <c>&lt;name&gt;.ours.rgb</c> per input JPEG.
        /// </summary>
        /// <param name="args">The input JPEG directory, then the output directory.</param>
        /// <returns>Zero once every file has been written.</returns>
        private static int Main(string[] args) {
            if (args.Length != 2) {
                Console.Error.WriteLine("usage: OurReading <jpegDir> <outDir>");
                return 1;
            }

            Directory.CreateDirectory(args[1]);

            foreach (string path in Directory.GetFiles(args[0], "*.jpg")) {
                string name = Path.GetFileNameWithoutExtension(path);
                JagexJpeg jpeg = JagexJpeg.Decode(File.ReadAllBytes(path));
                JpegRaster raster = BaselineJpegDecoder.Decode(jpeg);

                int[] argb;
                try {
                    argb = raster.ToArgb();
                }
                catch (InvalidDataException ex) {
                    // The editor refusing to colour a file is a result worth seeing rather than a
                    // crash: the repack's undeclared group 498 lands here, and the JVM draws it.
                    Console.WriteLine($"{name} REFUSED {ex.Message}");
                    continue;
                }

                using (FileStream stream = File.Create(Path.Combine(args[1], name + ".ours.rgb"))) {
                    stream.WriteByte((byte) (raster.Width >> 8));
                    stream.WriteByte((byte) raster.Width);
                    stream.WriteByte((byte) (raster.Height >> 8));
                    stream.WriteByte((byte) raster.Height);

                    byte[] buffer = new byte[argb.Length * 3];
                    for (int i = 0; i < argb.Length; i++) {
                        buffer[i * 3] = (byte) (argb[i] >> 16);
                        buffer[i * 3 + 1] = (byte) (argb[i] >> 8);
                        buffer[i * 3 + 2] = (byte) argb[i];
                    }
                    stream.Write(buffer, 0, buffer.Length);
                }

                Console.WriteLine($"{name} {raster.Width}x{raster.Height} components={raster.ComponentCount} " +
                                  $"scan={raster.ScanBytesConsumed}/{raster.ScanBytesAvailable} " +
                                  $"firstPixel={argb[0]:X8} fourthPlaneFlat=" +
                                  (raster.ComponentCount == 4 ? raster.IsConstant(3).ToString() : "n/a"));
            }

            return 0;
        }
    }
}
