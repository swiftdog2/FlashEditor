using FlashEditor.cache;

namespace FlashEditor.Definitions.Sprite {
    class Textures {

        /*
    private static int[] colors;

    public static void initialize(RSCache cache) {
        int count = 0;
        try {
            ReferenceTable table = cache.GetReferenceTable(Constants.TEXTURES);
            Entry entry = table.GetEntry(0);
            Archive archive = Archive.Decode(cache.GetContainer(Constants.TEXTURES, 0).GetStream(), entry.GetSize());

            int[] ids = new int[entry.Capacity()];
            colors = new int[entry.Capacity()];
            for(int id = 0; id < entry.Capacity(); id++) {
                ChildEntry child = entry.GetEntry(id);
                if(child == null)
                    continue;

                JagStream buffer = archive.GetEntry(child.GetId()).;
                Texture texture = Texture.Decode(buffer);
                ids[id] = texture.GetIds(0);
                count++;
            }

            table = cache.GetReferenceTable(CacheIndex.SPRITES);
            for(int id = 0; id < entry.capacity(); id++) {
                int file = ids[id];

                Entry e = table.GetEntry(file);
                if(e == null)
                    continue;

                Container containers = cache.Read(8, file);
                Sprite sprite = Sprite.Decode(containers.GetData());
                BufferedImage img = sprite.GetFrame(0);
                int width = img.GetWidth();
                int height = img.GetHeight();
                int[] pixels = new int[width * height];
                PixelGrabber pixelgrabber = new PixelGrabber(img, 0, 0, width, height, pixels, 0, width);
                try {
                    pixelgrabber.grabPixels();
                } catch(InterruptedException e1) {
                    // TODO Auto-generated catch block
                    e1.printStackTrace();
                }
                colors[id] = averageColorForPixels(pixels);
            }
        } catch(IOException e) {
            logger.log(Level.SEVERE, "Error Loading Texture(s)!", e);
        }
        logger.info("Loaded " + count + " Texture(s)!");
    }

    private static int averageColorForPixels(int[] pixels) {
        int redTotal = 0;
        int greenTotal = 0;
        int blueTotal = 0;
        int totalPixels = pixels.length;

        for(int i = 0; i < totalPixels; i++) {
            if(pixels[i] == 0xff00ff) {
                totalPixels--;
                continue;
            }

            redTotal += pixels[i] >> 16 & 0xff;
            greenTotal += pixels[i] >> 8 & 0xff;
            blueTotal += pixels[i] & 0xff;
        }

        int averageRGB = (redTotal / totalPixels << 16) + (greenTotal / totalPixels << 8) + blueTotal / totalPixels;
        if(averageRGB == 0) {
            averageRGB = 1;
        }

        return averageRGB;
    }

    public static int getColors(int i) {
        return colors[i];
    }
                    */
    }
}
