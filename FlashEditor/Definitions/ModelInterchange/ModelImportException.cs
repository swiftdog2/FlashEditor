using System;

namespace FlashEditor.Definitions.ModelInterchange {
    /// <summary>
    ///     Thrown when an OBJ cannot be read, or when the mesh it holds cannot be written back over
    ///     a model without losing something the format carries and OBJ does not.
    /// </summary>
    /// <remarks>
    ///     Every message names the line or the array that is the problem and the counts that say
    ///     so, because the user's next move is to fix the mesh rather than to read code.
    ///     <see cref="ModelObjImporter"/> turns this into a failed
    ///     <see cref="ModelImportResult"/> rather than letting it escape, so a caller only sees it
    ///     when it calls <see cref="ObjParser"/> or <see cref="ModelGeometryEncoder"/> directly.
    /// </remarks>
    public class ModelImportException : Exception {
        /// <summary>Creates an exception with no detail.</summary>
        public ModelImportException() { }

        /// <summary>Creates an exception describing what could not be mapped.</summary>
        /// <param name="message">What could not be mapped, and the counts that say so.</param>
        public ModelImportException(string message) : base(message) { }

        /// <summary>Creates an exception wrapping a lower-level failure.</summary>
        /// <param name="message">What could not be mapped.</param>
        /// <param name="innerException">The failure underneath.</param>
        public ModelImportException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
