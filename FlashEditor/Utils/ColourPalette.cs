namespace FlashEditor.Utils {
    /// <summary>
    /// 256-entry colour palette used by opcode 42 (recolorDstPalette).
    /// Populated at runtime by CS2 server scripts; entries default to 0 (unused).
    /// </summary>
    public static class ColourPalette {
        public static readonly short[] Entries = new short[256]; // TODO: populate from CS2 server data
    }
}
