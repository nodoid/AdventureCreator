using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Graphics;

/// <summary>
/// Produces the frame of an animated picture at a given time: blinking layers are hidden, moving layers offset,
/// colour-cycled layers recoloured and flipbook layers switched to their current sub-picture. The original picture
/// is never changed.
/// </summary>
public static class PictureAnimator
{
    /// <summary>The picture as it looks <paramref name="milliseconds"/> after it appeared.</summary>
    public static Picture Frame(Picture picture, double milliseconds)
    {
        if (!picture.HasAnimations) return picture;
        var commands = picture.Commands.Select(c => c.Clone()).ToList();
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var colours = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in picture.Animations)
        {
            if (!a.Enabled || string.IsNullOrWhiteSpace(a.Layer)) continue;
            double t = milliseconds - a.DelayMs;
            if (t < 0) continue;
            double period = Math.Max(50, a.PeriodMs);
            var layer = commands.Where(c => InLayer(c, a.Layer)).ToList();
            switch (a.Kind)
            {
                case AnimationKind.Blink:
                    if (t % period >= period * Math.Clamp(a.OnPercent, 0, 100) / 100.0) hidden.Add(a.Layer);
                    break;
                case AnimationKind.Move:
                {
                    var (dx, dy) = MoveOffset(a, t);
                    foreach (var c in layer) PictureGeometry.Translate(c, dx, dy);
                    break;
                }
                case AnimationKind.ColourCycle:
                    if (a.Colours.Count > 0) colours[a.Layer] = a.Colours[(int)(t / period) % a.Colours.Count];
                    break;
                case AnimationKind.Flipbook:
                    if (a.Frames.Count > 0)
                    {
                        var frame = a.Frames[(int)(t / period) % a.Frames.Count];
                        foreach (var c in layer.Where(c => c.Op == DrawOp.Call)) c.SubPictureId = frame;
                    }
                    break;
            }
        }

        var result = new List<DrawCommand>(commands.Count + 8);
        int ink = picture.InitialInk;
        foreach (var c in commands)
        {
            if (c.Layer != null && hidden.Contains(c.Layer)) continue;
            if (c.Layer != null && colours.TryGetValue(c.Layer, out var colour))
            {
                if (PictureGeometry.UsesInk(c.Op))
                {
                    result.Add(new DrawCommand { Op = DrawOp.SetInk, Color = colour });
                    result.Add(c);
                    result.Add(new DrawCommand { Op = DrawOp.SetInk, Color = ink });
                    continue;
                }
                if (c.Op is DrawOp.AttributeBlock or DrawOp.Clear or DrawOp.SetPaper) c.Color = colour;
            }
            if (c.Op == DrawOp.SetInk) ink = c.Color;
            result.Add(c);
        }
        return picture.WithCommands(result);
    }

    /// <summary>Offset of a Move animation <paramref name="t"/> ms after it started.</summary>
    public static (int Dx, int Dy) MoveOffset(PictureAnimation a, double t)
    {
        double period = Math.Max(50, a.PeriodMs);
        double f;
        if (a.PingPong)
        {
            f = t % (2 * period) / period;
            if (f > 1) f = 2 - f;
        }
        else f = t % period / period;
        return ((int)Math.Round(a.Dx * f), (int)Math.Round(a.Dy * f));
    }

    /// <summary>The layer names used by a picture's commands.</summary>
    public static IReadOnlyList<string> Layers(Picture picture) =>
        picture.Commands.Select(c => c.Layer).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>A cheap content hash of a picture's drawing (for caching rendered frames).</summary>
    public static int ContentHash(Picture picture)
    {
        var h = new HashCode();
        h.Add(picture.Width); h.Add(picture.Height); h.Add(picture.RenderMode); h.Add(picture.InitialInk); h.Add(picture.InitialPaper);
        h.Add(picture.BitmapAsset); h.Add(picture.LineWidth); h.Add(picture.Palette.Count);
        foreach (var p in picture.Palette) h.Add(p);
        foreach (var c in picture.Commands)
        {
            h.Add(c.Op); h.Add(c.X); h.Add(c.Y); h.Add(c.X2); h.Add(c.Y2); h.Add(c.Color); h.Add(c.Color2); h.Add(c.Scale);
            h.Add(c.Text); h.Add(c.SubPictureId); h.Add(c.Xor); h.Add(c.Inverse);
            foreach (var p in c.Points) h.Add(p);
            if (c.Pattern != null) foreach (var b in c.Pattern) h.Add(b);
        }
        return h.ToHashCode();
    }

    private static bool InLayer(DrawCommand c, string layer) => string.Equals(c.Layer, layer, StringComparison.OrdinalIgnoreCase);
}
