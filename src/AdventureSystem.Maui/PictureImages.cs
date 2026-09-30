using AdventureSystem.Core.Graphics;
using AdventureSystem.Core.Model;

namespace AdventureSystem.Maui;

/// <summary>Renders adventure pictures to MAUI image sources (integer-scaled so retro pixels stay crisp).</summary>
public static class PictureImages
{
    public static byte[] RenderPng(Picture picture, Adventure adventure, int scale = 3, int? commandLimit = null)
    {
        // Large pictures don't need upscaling.
        if (picture.Width >= 512) scale = 1;
        return PictureRenderer.RenderPng(picture, adventure, scale, commandLimit);
    }

    public static ImageSource Source(byte[] png) => ImageSource.FromStream(() => new MemoryStream(png));

    public static ImageSource? Source(Adventure adventure, string? pictureId, int scale = 3)
    {
        var pic = adventure.FindPicture(pictureId);
        return pic == null ? null : Source(RenderPng(pic, adventure, scale));
    }

    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return Color.FromArgb(hex); }
        catch { return fallback; }
    }

    public static Color FromArgb(uint argb) =>
        Color.FromRgba((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
