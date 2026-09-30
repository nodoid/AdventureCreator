using AdventureCreator.Core.Graphics;
using AdventureCreator.Core.Model;
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace AdventureCreator.Maui;

/// <summary>
/// Draws adventure pictures with Maui.Graphics. Smooth pictures are drawn as anti-aliased vectors at the size they
/// are shown; full-colour and Spectrum pictures are drawn from the software renderer's pixels, kept crisp. Handles
/// animation frames and caches whatever it can between frames.
/// </summary>
public sealed class PictureCanvas
{
    private const int FillGrid = 4;

    private int rasterKey;
    private IImage? rasterImage;
    private int fillsKey;
    private List<(FillRegion Region, PathF Ink, PathF Paper)>? fills;
    private readonly Dictionary<string, IImage?> assetImages = new();

    /// <summary>
    /// Draws <paramref name="picture"/> (at animation time <paramref name="milliseconds"/>) into <paramref name="dest"/>.
    /// <paramref name="commandLimit"/> draws only the first N commands (the editor's step view).
    /// </summary>
    public void Draw(ICanvas canvas, RectF dest, Picture picture, Adventure? adventure, double milliseconds = 0, int? commandLimit = null)
    {
        if (picture.Width <= 0 || picture.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return;
        var frame = PictureAnimator.Frame(picture, milliseconds);
        if (commandLimit is { } limit && limit < frame.Commands.Count) frame = frame.WithCommands(frame.Commands.Take(limit).ToList());

        canvas.SaveState();
        canvas.ClipRectangle(dest);
        if (picture.RenderMode == PictureRenderMode.Smooth) DrawSmooth(canvas, dest, frame, adventure);
        else DrawRaster(canvas, dest, frame, adventure);
        canvas.RestoreState();
    }

    // ------------------------------------------------------------------ pixel modes

    private void DrawRaster(ICanvas canvas, RectF dest, Picture frame, Adventure? adventure)
    {
        // Pre-scale by a whole number close to the display size so platform image scaling can't blur the pixels.
        int factor = Math.Clamp((int)Math.Ceiling(dest.Width / frame.Width), 1, 8);
        if (frame.Width >= 512) factor = 1;
        int key = HashCode.Combine(PictureAnimator.ContentHash(frame), factor, SubPicturesHash(frame, adventure));
        if (rasterImage == null || key != rasterKey)
        {
            var png = PictureRenderer.Render(frame, adventure).Scale(factor).ToPng();
            rasterImage = PlatformImage.FromStream(new MemoryStream(png));
            rasterKey = key;
        }
        canvas.Antialias = false;
        canvas.DrawImage(rasterImage, dest.X, dest.Y, dest.Width, dest.Height);
    }

    // ------------------------------------------------------------------ smooth (vector) mode

    private sealed class State
    {
        public int Ink, Paper;
        public int FillIndex;
        public int Depth;
        /// <summary>Sub-picture transforms currently applied (offset and scale), innermost last.</summary>
        public readonly List<(float X, float Y, float F)> Calls = new();
    }

    private void DrawSmooth(ICanvas canvas, RectF dest, Picture frame, Adventure? adventure)
    {
        int key = HashCode.Combine(PictureAnimator.ContentHash(frame), SubPicturesHash(frame, adventure));
        if (fills == null || key != fillsKey)
        {
            fills = PictureRenderer.TraceFills(frame, adventure, FillGrid)
                .Select(r => (r, RunsToPath(r.InkRuns), RunsToPath(r.PaperRuns))).ToList();
            fillsKey = key;
        }

        float scale = dest.Width / frame.Width;
        canvas.Antialias = true;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.Translate(dest.X, dest.Y);
        canvas.Scale(scale, scale);

        var state = new State { Ink = frame.InitialInk, Paper = frame.InitialPaper };
        canvas.FillColor = Colour(frame, frame.InitialPaper);
        canvas.FillRectangle(0, 0, frame.Width, frame.Height);
        if (!string.IsNullOrEmpty(frame.BitmapAsset) && AssetImage(adventure, frame.BitmapAsset) is { } bg)
            canvas.DrawImage(bg, 0, 0, frame.Width, frame.Height);

        DrawCommands(canvas, frame, frame, adventure, state, (float)Math.Max(0.25, frame.LineWidth));
    }

    private void DrawCommands(ICanvas canvas, Picture root, Picture picture, Adventure? adventure, State s, float lineWidth)
    {
        if (++s.Depth > 16) { s.Depth--; return; }
        foreach (var c in picture.Commands)
        {
            var ink = Colour(root, c.Inverse ? s.Paper : s.Ink);
            canvas.StrokeColor = ink;
            canvas.FillColor = ink;
            canvas.StrokeSize = lineWidth;
            switch (c.Op)
            {
                case DrawOp.Clear:
                    s.Paper = c.Color;
                    canvas.FillColor = Colour(root, c.Color);
                    canvas.FillRectangle(-1, -1, picture.Width + 2, picture.Height + 2);
                    break;
                case DrawOp.SetInk: s.Ink = c.Color; break;
                case DrawOp.SetPaper: s.Paper = c.Color; break;
                case DrawOp.Plot:
                    canvas.FillCircle(c.X + 0.5f, c.Y + 0.5f, Math.Max(0.5f, lineWidth / 2));
                    break;
                case DrawOp.Line:
                    canvas.DrawLine(c.X + 0.5f, c.Y + 0.5f, c.X2 + 0.5f, c.Y2 + 0.5f);
                    break;
                case DrawOp.Rectangle:
                    canvas.DrawRectangle(Box(c, 0.5f, 0.5f));
                    break;
                case DrawOp.FilledRectangle:
                    canvas.FillRectangle(Box(c, 0, 1));
                    break;
                case DrawOp.Ellipse:
                    canvas.DrawEllipse(Box(c, 0.5f, 0.5f));
                    break;
                case DrawOp.FilledEllipse:
                    canvas.FillEllipse(Box(c, 0, 1));
                    break;
                case DrawOp.Polygon:
                case DrawOp.FilledPolygon:
                case DrawOp.Freehand:
                {
                    if (c.Points.Count < 2) break;
                    var path = new PathF(c.Points[0] + 0.5f, c.Points[1] + 0.5f);
                    for (int i = 2; i + 1 < c.Points.Count; i += 2) path.LineTo(c.Points[i] + 0.5f, c.Points[i + 1] + 0.5f);
                    if (c.Op == DrawOp.Freehand)
                    {
                        float brush = lineWidth * Math.Max(1, c.Scale);
                        canvas.StrokeSize = brush;
                        if (c.Points.Count == 2) canvas.FillCircle(c.Points[0] + 0.5f, c.Points[1] + 0.5f, brush / 2);
                        else canvas.DrawPath(path);
                        break;
                    }
                    path.Close();
                    if (c.Op == DrawOp.FilledPolygon) canvas.FillPath(path);
                    canvas.DrawPath(path);
                    break;
                }
                case DrawOp.Fill:
                case DrawOp.Shade:
                {
                    // Regions were traced for the root picture, in drawing order; sub-pictures are drawn in root coordinates.
                    if (fills == null || s.FillIndex >= fills.Count) break;
                    var (region, inkPath, paperPath) = fills[s.FillIndex++];
                    canvas.SaveState();
                    for (int i = s.Calls.Count - 1; i >= 0; i--)
                    {
                        canvas.Scale(1 / s.Calls[i].F, 1 / s.Calls[i].F);
                        canvas.Translate(-s.Calls[i].X, -s.Calls[i].Y);
                    }
                    canvas.Antialias = false;
                    float cell = 1f / FillGrid;
                    canvas.Scale(cell, cell);
                    canvas.FillColor = Colour(root, region.Ink);
                    canvas.FillPath(inkPath);
                    if (region.PaperRuns.Count > 0)
                    {
                        canvas.FillColor = Colour(root, region.Paper);
                        canvas.FillPath(paperPath);
                    }
                    canvas.RestoreState();
                    break;
                }
                case DrawOp.AttributeBlock:
                    canvas.FillColor = Colour(root, c.Color2 >= 0 ? c.Color2 : c.Color);
                    canvas.FillRectangle(Box(c, 0, 1));
                    break;
                case DrawOp.Text:
                {
                    int size = Math.Max(1, c.Scale == 8 ? 1 : c.Scale);
                    canvas.FontColor = ink;
                    canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                    canvas.FontSize = 8.5f * size;
                    var text = c.Text ?? "";
                    canvas.DrawString(text, c.X, c.Y - 1.5f * size, text.Length * 8 * size + 40, 12 * size, HorizontalAlignment.Left, VerticalAlignment.Top);
                    break;
                }
                case DrawOp.Call:
                {
                    if (adventure?.FindPicture(c.SubPictureId) is not { } sub || sub == picture) break;
                    canvas.SaveState();
                    canvas.Translate(c.X, c.Y);
                    float f = Math.Max(1, c.Scale) / 8f;
                    canvas.Scale(f, f);
                    s.Calls.Add((c.X, c.Y, f));
                    DrawCommands(canvas, root, sub, adventure, s, lineWidth / f);
                    s.Calls.RemoveAt(s.Calls.Count - 1);
                    canvas.RestoreState();
                    break;
                }
                case DrawOp.Image:
                {
                    if (c.Text == null || AssetImage(adventure, c.Text) is not { } img) break;
                    var (w, h) = PictureGeometry.ImageSize(c, adventure);
                    canvas.DrawImage(img, c.X, c.Y, w, h);
                    break;
                }
            }
        }
        s.Depth--;
    }

    /// <summary>A command's box: <paramref name="inset"/> moves the corners to pixel centres (outlines);
    /// <paramref name="extra"/> = 1 covers the last row and column of pixels (filled shapes).</summary>
    private static RectF Box(DrawCommand c, float inset, float extra)
    {
        float x0 = Math.Min(c.X, c.X2), y0 = Math.Min(c.Y, c.Y2), x1 = Math.Max(c.X, c.X2), y1 = Math.Max(c.Y, c.Y2);
        return RectF.FromLTRB(x0 + inset, y0 + inset, x1 + inset + extra, y1 + inset + extra);
    }

    private static PathF RunsToPath(List<(int Y, int X0, int X1)> runs)
    {
        var path = new PathF();
        foreach (var (y, x0, x1) in runs)
        {
            path.MoveTo(x0, y);
            path.LineTo(x1 + 1, y);
            path.LineTo(x1 + 1, y + 1);
            path.LineTo(x0, y + 1);
            path.Close();
        }
        return path;
    }

    private static Color Colour(Picture picture, int index)
    {
        var pal = picture.Palette.Count > 0 ? picture.Palette : Palettes.Spectrum.ToList();
        return index < 0 ? Colors.Transparent : PictureImages.FromArgb(pal[index % pal.Count]);
    }

    private IImage? AssetImage(Adventure? adventure, string name)
    {
        if (assetImages.TryGetValue(name, out var cached)) return cached;
        IImage? img = null;
        if (adventure != null && adventure.Assets.TryGetValue(name, out var bytes))
        {
            try { img = PlatformImage.FromStream(new MemoryStream(bytes)); }
            catch { img = null; }
        }
        return assetImages[name] = img;
    }

    private static int SubPicturesHash(Picture picture, Adventure? adventure)
    {
        if (adventure == null) return 0;
        var h = new HashCode();
        foreach (var c in picture.Commands)
            if (c.Op == DrawOp.Call && adventure.FindPicture(c.SubPictureId) is { } sub && sub != picture)
                h.Add(PictureAnimator.ContentHash(sub));
        return h.ToHashCode();
    }
}

/// <summary>
/// Shows an adventure picture, scaled to fit and centred, and plays its animations. Drawn with Maui.Graphics, so
/// Smooth pictures stay sharp at any size.
/// </summary>
public sealed class PictureView : GraphicsView, IDrawable
{
    private readonly PictureCanvas renderer = new();
    private readonly System.Diagnostics.Stopwatch clock = new();
    private IDispatcherTimer? timer;
    private Picture? picture;
    private Adventure? adventure;

    public PictureView()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => StopTimer();
    }

    /// <summary>Shows a picture (null clears it) and restarts its animations.</summary>
    public void Show(Picture? picture, Adventure? adventure)
    {
        this.picture = picture;
        this.adventure = adventure;
        clock.Restart();
        UpdateTimer();
        Invalidate();
    }

    public Picture? Picture => picture;

    /// <summary>Frames per second for animated pictures.</summary>
    public int FramesPerSecond { get; set; } = 20;

    private void UpdateTimer()
    {
        if (picture?.HasAnimations == true && Handler != null)
        {
            if (timer == null)
            {
                timer = Dispatcher.CreateTimer();
                timer.Tick += (_, _) => { if (IsVisible) Invalidate(); };
            }
            timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(FramesPerSecond, 1, 60));
            if (!timer.IsRunning) timer.Start();
        }
        else StopTimer();
    }

    private void StopTimer() => timer?.Stop();

    void IDrawable.Draw(ICanvas canvas, RectF rect)
    {
        if (picture == null || picture.Width <= 0 || picture.Height <= 0) return;
        float scale = Math.Min(rect.Width / picture.Width, rect.Height / picture.Height);
        // Pixel pictures look best at whole-number scales when there's room.
        if (picture.RenderMode != PictureRenderMode.Smooth && scale >= 2) scale = MathF.Floor(scale);
        float w = picture.Width * scale, h = picture.Height * scale;
        var dest = new RectF(rect.X + (rect.Width - w) / 2, rect.Y + (rect.Height - h) / 2, w, h);
        renderer.Draw(canvas, dest, picture, adventure, clock.Elapsed.TotalMilliseconds);
    }
}
