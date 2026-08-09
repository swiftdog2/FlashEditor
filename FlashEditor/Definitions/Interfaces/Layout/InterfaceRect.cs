using System;

namespace FlashEditor.Definitions.Interfaces.Layout {
    /// <summary>
    ///     A rectangle in interface space, in whole pixels.
    /// </summary>
    /// <remarks>
    ///     Not <see cref="System.Drawing.Rectangle"/>, on purpose. This type is produced by a port
    ///     of the client's integer arithmetic and is consumed by tests that run headless; pulling
    ///     <c>System.Drawing</c> into the layout path would put a UI dependency underneath a
    ///     calculation that has nothing to do with drawing, and <c>Region</c>-style namespace
    ///     collisions with WinForms are already a documented cost in this repository. The canvas
    ///     converts at its own edge.
    ///     <para>
    ///     <b>Extents can legitimately be negative.</b> Mode 1 resolves an extent as
    ///     <c>parent - base</c>, and nothing in the client clamps the result, so a component whose
    ///     base exceeds its parent produces a negative width. That is what the client computes and
    ///     what it then draws nothing for, so it is represented rather than corrected.
    ///     </para>
    /// </remarks>
    public readonly struct InterfaceRect : IEquatable<InterfaceRect> {
        /// <summary>
        ///     The canvas a root interface resolves against in fixed display mode.
        /// </summary>
        /// <remarks>
        ///     765 x 503, from <c>client.java:1654-1655</c> (<c>screenXsize = 765</c>,
        ///     <c>screenYsize = 503</c>), copied into the two globals the layout pass reads at
        ///     <c>Class93_Sub1_Sub1.java:100-101</c>. The client's other modes are 640 x 480
        ///     (<c>client.java:1650-1651</c>) and resizable, which clamps at 1024x768 and 800x600
        ///     (<c>Class299.java:60-77</c>); fixed is the default because it is the mode the shipped
        ///     interfaces were authored against.
        /// </remarks>
        public static readonly InterfaceRect FixedModeCanvas = new InterfaceRect(0, 0, 765, 503);

        /// <summary>A rectangle.</summary>
        /// <param name="x">The left edge.</param>
        /// <param name="y">The top edge.</param>
        /// <param name="width">The width, which may be negative.</param>
        /// <param name="height">The height, which may be negative.</param>
        public InterfaceRect(int x, int y, int width, int height) {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>The left edge.</summary>
        public int X { get; }

        /// <summary>The top edge.</summary>
        public int Y { get; }

        /// <summary>The width. Negative where the client's own arithmetic produces one.</summary>
        public int Width { get; }

        /// <summary>The height. Negative where the client's own arithmetic produces one.</summary>
        public int Height { get; }

        /// <summary>The exclusive right edge.</summary>
        public int Right => X + Width;

        /// <summary>The exclusive bottom edge.</summary>
        public int Bottom => Y + Height;

        /// <summary>Whether the rectangle encloses no pixels.</summary>
        public bool IsEmpty => Width <= 0 || Height <= 0;

        /// <summary>
        ///     The overlap of two rectangles, or an empty rectangle at the origin where they do not
        ///     meet.
        /// </summary>
        /// <remarks>
        ///     Written as an explicit max/min of the four edges rather than deferring to a library,
        ///     because this mirrors what <c>Node_Sub10_Sub24.java:194-203</c> does and the whole
        ///     value of the port is that the two can be read side by side.
        /// </remarks>
        /// <param name="other">The other rectangle.</param>
        /// <returns>The overlap.</returns>
        public InterfaceRect Intersect(InterfaceRect other) {
            int left = Math.Max(X, other.X);
            int top = Math.Max(Y, other.Y);
            int right = Math.Min(Right, other.Right);
            int bottom = Math.Min(Bottom, other.Bottom);

            return right <= left || bottom <= top
                ? default
                : new InterfaceRect(left, top, right - left, bottom - top);
        }

        /// <summary>The rectangle moved by an offset, keeping its size.</summary>
        /// <param name="dx">The horizontal offset.</param>
        /// <param name="dy">The vertical offset.</param>
        /// <returns>The moved rectangle.</returns>
        public InterfaceRect Offset(int dx, int dy) {
            return new InterfaceRect(X + dx, Y + dy, Width, Height);
        }

        /// <summary>
        ///     Whether this rectangle lies wholly inside another.
        /// </summary>
        /// <remarks>
        ///     Half-open on the right and bottom, which is what makes
        ///     <c>Contains(this)</c> true and matches how the clip intersection above treats the
        ///     edges. An empty rectangle is contained by anything, because it encloses no pixel that
        ///     could fall outside.
        /// </remarks>
        /// <param name="outer">The rectangle to test against.</param>
        /// <returns>Whether every pixel of this one is inside it.</returns>
        public bool IsInside(InterfaceRect outer) {
            if (IsEmpty)
                return true;

            return X >= outer.X && Y >= outer.Y && Right <= outer.Right && Bottom <= outer.Bottom;
        }

        /// <inheritdoc/>
        public bool Equals(InterfaceRect other) {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) {
            return obj is InterfaceRect other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode() {
            return HashCode.Combine(X, Y, Width, Height);
        }

        /// <summary>The rectangle in words, for a test failure and a status line.</summary>
        /// <returns>The description.</returns>
        public override string ToString() {
            return "(" + X + ", " + Y + ") " + Width + "x" + Height;
        }
    }
}
