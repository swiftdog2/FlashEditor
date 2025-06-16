namespace FlashEditor
{
    /// <summary>
    /// Common interface for cache definitions supporting encode/decode.
    /// </summary>
    internal interface IDefinition
    {
        /// <summary>Populates the definition by decoding the supplied stream.</summary>
        void Decode(JagStream stream);

        /// <summary>Encodes the definition back into a stream.</summary>
        JagStream Encode();
    }
}
