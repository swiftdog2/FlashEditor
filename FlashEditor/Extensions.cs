using OpenTK.Mathematics;

internal static class WinFormsExtensions
{
    public static Vector2i ToVector2i(this System.Drawing.Size size) =>
        new(size.Width, size.Height);
}
