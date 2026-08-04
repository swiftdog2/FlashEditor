namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     Which of the two payload formats an index-32 group holds.
    /// </summary>
    /// <remarks>
    ///     The index is mixed and the constant's name does not say so - <c>RSConstants.cs</c> calls
    ///     it "in jpg format" and five of the twenty-six groups are not JPEG at all. The client
    ///     never has to choose, because it reaches the two through different call sites:
    ///     <c>Class237_Sub1.java:13-32</c> hands a group to the AWT image decoder, while
    ///     <c>Class324.method3684</c> hands a group at the same index to the Jagex sprite decoder,
    ///     and <c>Class84.java:20-31</c> only ever asks for the three it knows are glyph sheets.
    ///     An editor enumerating the whole index has no such caller to ask, so it dispatches on the
    ///     payload's own magic instead.
    /// </remarks>
    public enum LoadingSpriteShape {
        /// <summary>A JPEG image, opening <c>FF D8</c>.</summary>
        JpegImage,

        /// <summary>A Jagex sprite set, read backwards from the end of the file.</summary>
        SpriteSet
    }
}
