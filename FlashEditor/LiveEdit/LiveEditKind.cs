namespace FlashEditor.LiveEdit {
    /// <summary>
    ///     What a live-edit frame carries, as the wire byte both legs of
    ///     <c>reference/live-edit-protocol.md</c> share. The numeric values are the protocol and
    ///     must not be renumbered.
    /// </summary>
    public enum LiveEditKind : byte {
        /// <summary>An <c>m</c> terrain file, addressed by square id <c>(x &lt;&lt; 8) | y</c>.</summary>
        Terrain = 0,

        /// <summary>An <c>l</c> location file, addressed by square id.</summary>
        Locations = 1,

        /// <summary>A <c>um</c> underwater terrain file, addressed by square id.</summary>
        UnderwaterTerrain = 2,

        /// <summary>A <c>ul</c> underwater location file, addressed by square id.</summary>
        UnderwaterLocations = 3,

        /// <summary>A floor underlay definition (index 2, group 1), addressed by its file id.</summary>
        FloorUnderlay = 4,

        /// <summary>A floor overlay definition (index 2, group 4), addressed by its file id.</summary>
        FloorOverlay = 5,

        /// <summary>
        ///     A scene rebuild request. Server to client only: the editor never sends it and the
        ///     server rejects it on leg A.
        /// </summary>
        Rebuild = 6,

        /// <summary>
        ///     The end of one editor flush. Editor to server only, always id 0 with no payload: the
        ///     server holds a connection's frames until it arrives and applies them in one drain,
        ///     and discards them if the connection closes first.
        /// </summary>
        BatchEnd = 7
    }
}