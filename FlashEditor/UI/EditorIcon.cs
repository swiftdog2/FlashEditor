namespace FlashEditor.UI {
    /// <summary>
    ///     One icon in the editor's line-icon set, named for the verb it means.
    /// </summary>
    /// <remarks>
    ///     <b>Named per verb, never per picture.</b> A member called <c>MagnifyingGlass</c> pins the
    ///     drawing and leaves the meaning open, so the next reader who wants "find" and the one who
    ///     wants "zoom" both reach for it and one of them is wrong. <see cref="Search"/> and
    ///     <see cref="ZoomIn"/> can share a drawing without sharing a name.
    ///     <para>
    ///     The set is deliberately small. Every member here is drawn by
    ///     <see cref="EditorIcons"/> and verified by eye at 16px on both surfaces, and an icon
    ///     nobody has looked at is worse than a text label. Add one when a surface needs it, not in
    ///     anticipation.
    ///     </para>
    /// </remarks>
    public enum EditorIcon {
        /// <summary>Step back through the navigation history.</summary>
        Back,

        /// <summary>Step forward through the navigation history.</summary>
        Forward,

        /// <summary>Follow a reference to whatever it names.</summary>
        Link,

        /// <summary>Find a record.</summary>
        Search,

        /// <summary>Explain something the surface cannot say in its caption.</summary>
        Info,

        /// <summary>Warn that an action costs more than it looks like it does.</summary>
        Warning,

        /// <summary>Show a collapsed node's children.</summary>
        Expand,

        /// <summary>Hide an expanded node's children.</summary>
        Collapse,

        /// <summary>Read the record again from the cache.</summary>
        Refresh,

        /// <summary>Create a new record.</summary>
        Add,

        /// <summary>Take a record out.</summary>
        Remove,

        /// <summary>Copy a record to a new id.</summary>
        Duplicate,

        /// <summary>Move a record earlier in its parent's draw order.</summary>
        MoveUp,

        /// <summary>Move a record later in its parent's draw order.</summary>
        MoveDown,

        /// <summary>Undo the last edit.</summary>
        Undo,

        /// <summary>Redo the last undone edit.</summary>
        Redo,

        /// <summary>A colour value, and the picker behind it.</summary>
        Colour,

        /// <summary>A sprite or any other raster the cache stores.</summary>
        Image,

        /// <summary>A model from index 7.</summary>
        Model,

        /// <summary>A font from index 13.</summary>
        Font,

        /// <summary>An animation from index 20.</summary>
        Animation,

        /// <summary>A sound, from index 4, 14 or 15.</summary>
        Sound,

        /// <summary>A procedural texture from index 9.</summary>
        Texture,

        /// <summary>A client script from index 12.</summary>
        Script,

        /// <summary>Draw the view larger.</summary>
        ZoomIn,

        /// <summary>Draw the view smaller.</summary>
        ZoomOut,

        /// <summary>Show or hide the alignment grid.</summary>
        Grid,

        /// <summary>The record is drawn.</summary>
        Visible,

        /// <summary>The record is present and not drawn.</summary>
        Hidden,

        /// <summary>Select and pick things up.</summary>
        Pointer,

        /// <summary>Drag a thing to a new position.</summary>
        Move,

        /// <summary>Drag a thing's edge to a new size.</summary>
        Resize,

        /// <summary>Take a value off whatever is under the pointer.</summary>
        Eyedropper
    }
}
