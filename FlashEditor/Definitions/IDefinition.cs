namespace FlashEditor
{
    /// <summary>
    /// Common interface for cache definitions supporting encode/decode.
    /// </summary>
    internal interface IDefinition
    {
        /// <summary>
        /// Populates the definition by decoding the supplied stream.
        /// </summary>
        /// <param name="stream">Stream with full model+footer.</param>
        /// <param name="xteaKey">Optional XTEA decryption key (null by default).</param>
        void Decode(JagStream stream, int[] xteaKey = null);

        /// <summary>
        /// Encodes the definition back into a stream.
        /// </summary>
        JagStream Encode();
    }
}
