namespace FlashEditor.cache {
    /// <summary>
    /// Supported container compression algorithms.
    /// Values correspond to constants in <see cref="RSConstants"/>.
    /// </summary>
    public enum Compression : byte {
        None = RSConstants.NO_COMPRESSION,
        BZip2 = RSConstants.BZIP2_COMPRESSION,
        GZip = RSConstants.GZIP_COMPRESSION
    }
}
