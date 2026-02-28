using OpenTK.Mathematics;

/// <summary>Extension methods for bridging WinForms types to OpenTK.</summary>
internal static class WinFormsExtensions
{
    /// <summary>Converts a <see cref="System.Drawing.Size"/> to an OpenTK <see cref="Vector2i"/>.</summary>
    public static Vector2i ToVector2i(this System.Drawing.Size size) =>
        new(size.Width, size.Height);
}
