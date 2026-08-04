namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     Which index-5 file family a map square was read from.
    /// </summary>
    /// <remarks>
    ///     A square has to carry this because the save path cannot infer it. Surface and underwater
    ///     squares go through the same decoder and the same encoder, and differ only in how many
    ///     planes they hold - so a loader that did not record the family would leave
    ///     <see cref="MapSquareLoader.Save"/> resolving <c>m</c> and <c>l</c> for every square it is
    ///     handed, and one plane of seabed would land on top of the four-plane surface square with
    ///     nothing failing.
    ///
    ///     Plane count is not a substitute. A surface square whose upper planes are empty still
    ///     decodes to four planes, but the inverse - deciding the family from the count - would be a
    ///     guess about which group to overwrite, and the cost of guessing wrong is the user's cache.
    /// </remarks>
    public enum MapSquareLayer {
        /// <summary>The <c>m</c> and <c>l</c> families: four planes of terrain and their objects.</summary>
        Surface,

        /// <summary>The <c>um</c> and <c>ul</c> families: one plane of seabed and its objects.</summary>
        Underwater
    }
}
