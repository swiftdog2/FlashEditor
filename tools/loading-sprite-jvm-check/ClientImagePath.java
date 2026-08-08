import java.awt.Image;
import java.awt.MediaTracker;
import java.awt.Toolkit;
import java.awt.image.PixelGrabber;
import java.io.File;
import java.io.FileOutputStream;
import java.nio.file.Files;

/**
 * Replays the 637 client's own image path over real index-32 payloads, so that
 * "what does the client draw" can be answered by asking a JVM instead of by
 * inferring it.
 *
 * <p>{@code Class271.method3277} (Class271.java:29-65) does exactly this:
 * {@code Toolkit.createImage} on the raw bytes, {@code MediaTracker.waitForAll},
 * then {@code PixelGrabber} into an int[] of ARGB. Whatever lands in that array
 * is what the client turns into an ImageArchive, so it is the whole answer.
 * {@code Class116.method2162} runs the same call over the client's embedded
 * probe blob and, when it throws, {@code InterfaceSettings.java:72-74} opens
 * index 34 instead of index 32.
 *
 * <p><b>The result depends on the JVM, which is the point.</b> Measured on this
 * repository's caches: JDK 8 decodes all of them, and its pixels agree with
 * {@code JpegRaster.ToArgb} to within three levels on every channel. JDK 11
 * refuses every one of them, and the client's own probe blob with them, raising
 * {@code sun.awt.image.ImageFormatException: Unsupported color conversion
 * request} from the native {@code JPEGImageDecoder.readImage}. So this must
 * never become a build gate - it is a measurement whose answer is a property of
 * the JVM you point at it. Run it under a JDK 8 to reproduce the client's era.
 *
 * <p>Usage: {@code javac -d classes ClientImagePath.java} then
 * {@code java -cp classes ClientImagePath <jpegDir> <outDir>}. Writes one
 * {@code <name>.toolkit.rgb} per accepted file: width and height as two
 * big-endian shorts, then one 3-byte RGB triple per pixel, which is the format
 * the comparator reads.
 */
public final class ClientImagePath {

    private ClientImagePath() {
    }

    public static void main(String[] args) throws Exception {
        File outDir = new File(args[1]);
        outDir.mkdirs();
        java.awt.Component observer = new java.awt.Container();
        int ok = 0;
        int refused = 0;

        File[] files = new File(args[0]).listFiles();
        if (files == null) {
            throw new IllegalArgumentException("not a directory: " + args[0]);
        }
        java.util.Arrays.sort(files);

        for (File f : files) {
            if (!f.getName().endsWith(".jpg")) {
                continue;
            }
            byte[] bytes = Files.readAllBytes(f.toPath());
            String status;
            int[] pixels = null;
            int w = -1;
            int h = -1;

            try {
                Image image = Toolkit.getDefaultToolkit().createImage(bytes);
                MediaTracker tracker = new MediaTracker(observer);
                tracker.addImage(image, 0);
                tracker.waitForAll();
                w = image.getWidth(observer);
                h = image.getHeight(observer);
                if (tracker.isErrorAny() || w < 0 || h < 0) {
                    status = "REFUSED errorAny=" + tracker.isErrorAny() + " w=" + w + " h=" + h;
                } else {
                    pixels = new int[w * h];
                    PixelGrabber grabber = new PixelGrabber(image, 0, 0, w, h, pixels, 0, w);
                    if (grabber.grabPixels()) {
                        status = "OK " + w + "x" + h + " firstPixel=" + String.format("%08X", pixels[0]);
                    } else {
                        status = "GRAB_FAILED status=" + grabber.getStatus();
                        pixels = null;
                    }
                }
            } catch (Throwable t) {
                status = "THREW " + t;
            }

            if (pixels != null) {
                ok++;
                write(new File(outDir, f.getName().replace(".jpg", ".toolkit.rgb")), w, h, pixels);
            } else {
                refused++;
            }
            System.out.println(f.getName() + " " + status);
        }

        System.out.println("--- decoded=" + ok + " refused=" + refused + " jvm="
            + System.getProperty("java.version") + " ---");
        System.exit(0);
    }

    private static void write(File target, int w, int h, int[] pixels) throws Exception {
        try (FileOutputStream out = new FileOutputStream(target)) {
            out.write(new byte[] { (byte) (w >> 8), (byte) w, (byte) (h >> 8), (byte) h });
            byte[] buf = new byte[pixels.length * 3];
            for (int i = 0; i < pixels.length; i++) {
                buf[i * 3] = (byte) (pixels[i] >> 16);
                buf[i * 3 + 1] = (byte) (pixels[i] >> 8);
                buf[i * 3 + 2] = (byte) pixels[i];
            }
            out.write(buf);
        }
    }
}
