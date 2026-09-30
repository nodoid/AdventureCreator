using AdventureSystem.Core.Model;

namespace AdventureSystem.Core.Graphics;

/// <summary>
/// Geometry of drawing commands for the WYSIWYG picture editor: bounds, hit testing, handles, moving, resizing and
/// recolouring. All coordinates are picture pixels.
/// </summary>
public static class PictureGeometry
{
    /// <summary>True for commands that draw something that can be picked and moved on the canvas.</summary>
    public static bool IsVisual(DrawOp op) => op is not (DrawOp.Clear or DrawOp.SetInk or DrawOp.SetPaper or DrawOp.SetBright);

    /// <summary>True for commands whose pixels are drawn in the current ink (so recolouring means changing that ink).</summary>
    public static bool UsesInk(DrawOp op) => op is DrawOp.Plot or DrawOp.Line or DrawOp.Rectangle or DrawOp.FilledRectangle
        or DrawOp.Ellipse or DrawOp.FilledEllipse or DrawOp.Polygon or DrawOp.FilledPolygon or DrawOp.Fill or DrawOp.Shade
        or DrawOp.Text or DrawOp.Freehand;

    private static bool IsTwoCorner(DrawOp op) => op is DrawOp.Line or DrawOp.Rectangle or DrawOp.FilledRectangle
        or DrawOp.Ellipse or DrawOp.FilledEllipse or DrawOp.AttributeBlock;

    /// <summary>Bounding box (inclusive) of a command, or null if it draws nothing by itself.</summary>
    public static (int X0, int Y0, int X1, int Y1)? Bounds(DrawCommand c, Adventure? adventure = null)
    {
        switch (c.Op)
        {
            case var op when IsTwoCorner(op):
                return (Math.Min(c.X, c.X2), Math.Min(c.Y, c.Y2), Math.Max(c.X, c.X2), Math.Max(c.Y, c.Y2));
            case DrawOp.Plot:
            case DrawOp.Fill:
            case DrawOp.Shade:
                return (c.X, c.Y, c.X, c.Y);
            case DrawOp.Polygon:
            case DrawOp.FilledPolygon:
            case DrawOp.Freehand:
            {
                if (c.Points.Count < 2) return null;
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                for (int i = 0; i + 1 < c.Points.Count; i += 2)
                {
                    x0 = Math.Min(x0, c.Points[i]); x1 = Math.Max(x1, c.Points[i]);
                    y0 = Math.Min(y0, c.Points[i + 1]); y1 = Math.Max(y1, c.Points[i + 1]);
                }
                int pad = c.Op == DrawOp.Freehand ? Math.Max(1, c.Scale) / 2 : 0;
                return (x0 - pad, y0 - pad, x1 + pad, y1 + pad);
            }
            case DrawOp.Text:
            {
                int s = Math.Max(1, c.Scale == 8 ? 1 : c.Scale);
                int len = Math.Max(1, (c.Text ?? "").Length);
                return (c.X, c.Y, c.X + len * 8 * s - 1, c.Y + 8 * s - 1);
            }
            case DrawOp.Call:
            {
                var sub = adventure?.FindPicture(c.SubPictureId);
                int w = sub?.Width ?? 16, h = sub?.Height ?? 16;
                int s = Math.Max(1, c.Scale);
                return (c.X, c.Y, c.X + Math.Max(1, w * s / 8) - 1, c.Y + Math.Max(1, h * s / 8) - 1);
            }
            case DrawOp.Image:
            {
                var (w, h) = ImageSize(c, adventure);
                return (c.X, c.Y, c.X + Math.Max(1, w) - 1, c.Y + Math.Max(1, h) - 1);
            }
            default:
                return null;
        }
    }

    /// <summary>Displayed size of an Image command: its explicit size, or the asset's natural size.</summary>
    public static (int W, int H) ImageSize(DrawCommand c, Adventure? adventure)
    {
        if (c.X2 > 0 && c.Y2 > 0) return (c.X2, c.Y2);
        int w = 32, h = 32;
        if (c.Text != null && adventure != null && adventure.Assets.TryGetValue(c.Text, out var png) && png.Length >= 24 && RasterImage.IsPng(png))
        {
            w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        }
        return (c.X2 > 0 ? c.X2 : w, c.Y2 > 0 ? c.Y2 : h);
    }

    /// <summary>
    /// Index of the topmost visual command at (x, y), or -1. <paramref name="tolerance"/> is in picture pixels.
    /// Outlines are only hit near their edges, filled shapes anywhere inside.
    /// </summary>
    public static int HitTest(Picture picture, Adventure? adventure, double x, double y, double tolerance = 2)
    {
        for (int i = picture.Commands.Count - 1; i >= 0; i--)
            if (Hits(picture.Commands[i], adventure, x, y, tolerance)) return i;
        return -1;
    }

    public static bool Hits(DrawCommand c, Adventure? adventure, double x, double y, double t)
    {
        if (!IsVisual(c.Op)) return false;
        switch (c.Op)
        {
            case DrawOp.Line:
                return SegmentDistance(x, y, c.X, c.Y, c.X2, c.Y2) <= t;
            case DrawOp.Rectangle:
            {
                var (x0, y0, x1, y1) = Bounds(c)!.Value;
                bool inOuter = x >= x0 - t && x <= x1 + t && y >= y0 - t && y <= y1 + t;
                bool inInner = x > x0 + t && x < x1 - t && y > y0 + t && y < y1 - t;
                return inOuter && !inInner;
            }
            case DrawOp.Ellipse:
            case DrawOp.FilledEllipse:
            {
                var (x0, y0, x1, y1) = Bounds(c)!.Value;
                double cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0;
                double rx = Math.Max(0.5, (x1 - x0) / 2.0), ry = Math.Max(0.5, (y1 - y0) / 2.0);
                double outer = Sq((x - cx) / (rx + t)) + Sq((y - cy) / (ry + t));
                if (outer > 1) return false;
                if (c.Op == DrawOp.FilledEllipse || rx <= t || ry <= t) return true;
                return Sq((x - cx) / (rx - t)) + Sq((y - cy) / (ry - t)) >= 1;
            }
            case DrawOp.Polygon:
            case DrawOp.FilledPolygon:
            case DrawOp.Freehand:
            {
                var p = c.Points;
                if (p.Count < 2) return false;
                double reach = t + (c.Op == DrawOp.Freehand ? Math.Max(1, c.Scale) / 2.0 : 0);
                if (p.Count == 2) return Math.Abs(x - p[0]) <= reach && Math.Abs(y - p[1]) <= reach;
                for (int i = 0; i + 3 < p.Count; i += 2)
                    if (SegmentDistance(x, y, p[i], p[i + 1], p[i + 2], p[i + 3]) <= reach) return true;
                if (c.Op != DrawOp.Freehand && SegmentDistance(x, y, p[^2], p[^1], p[0], p[1]) <= reach) return true;
                return c.Op == DrawOp.FilledPolygon && InsidePolygon(p, x, y);
            }
            case DrawOp.Plot:
            case DrawOp.Fill:
            case DrawOp.Shade:
            {
                double r = Math.Max(t, 3);
                return Math.Abs(x - c.X) <= r && Math.Abs(y - c.Y) <= r;
            }
            default:
            {
                if (Bounds(c, adventure) is not { } b) return false;
                return x >= b.X0 - t && x <= b.X1 + t && y >= b.Y0 - t && y <= b.Y1 + t;
            }
        }
    }

    /// <summary>The draggable handles of a command (endpoints, corners or vertices). Empty if it can only be moved.</summary>
    public static IReadOnlyList<(int X, int Y)> Handles(DrawCommand c, Adventure? adventure = null)
    {
        switch (c.Op)
        {
            case DrawOp.Line:
                return new[] { (c.X, c.Y), (c.X2, c.Y2) };
            case var op when IsTwoCorner(op):
                return new[] { (c.X, c.Y), (c.X2, c.Y), (c.X, c.Y2), (c.X2, c.Y2) };
            case DrawOp.Polygon:
            case DrawOp.FilledPolygon:
            {
                var list = new List<(int, int)>();
                for (int i = 0; i + 1 < c.Points.Count; i += 2) list.Add((c.Points[i], c.Points[i + 1]));
                return list;
            }
            case DrawOp.Image:
            {
                var (w, h) = ImageSize(c, adventure);
                return new[] { (c.X + w, c.Y + h) };
            }
            default:
                return Array.Empty<(int, int)>();
        }
    }

    /// <summary>Index of the handle within <paramref name="radius"/> of (x, y), or -1.</summary>
    public static int HandleAt(DrawCommand c, Adventure? adventure, double x, double y, double radius)
    {
        var handles = Handles(c, adventure);
        for (int i = handles.Count - 1; i >= 0; i--)
            if (Math.Abs(handles[i].X - x) <= radius && Math.Abs(handles[i].Y - y) <= radius) return i;
        return -1;
    }

    /// <summary>Moves handle <paramref name="handle"/> of <paramref name="c"/> to (x, y).</summary>
    public static void MoveHandle(DrawCommand c, int handle, int x, int y, Adventure? adventure = null)
    {
        switch (c.Op)
        {
            case DrawOp.Line:
                if (handle == 0) { c.X = x; c.Y = y; } else { c.X2 = x; c.Y2 = y; }
                break;
            case var op when IsTwoCorner(op):
                if (handle is 0 or 2) c.X = x; else c.X2 = x;
                if (handle is 0 or 1) c.Y = y; else c.Y2 = y;
                break;
            case DrawOp.Polygon:
            case DrawOp.FilledPolygon:
                if (handle * 2 + 1 < c.Points.Count) { c.Points[handle * 2] = x; c.Points[handle * 2 + 1] = y; }
                break;
            case DrawOp.Image:
                c.X2 = Math.Max(1, x - c.X);
                c.Y2 = Math.Max(1, y - c.Y);
                break;
        }
    }

    /// <summary>Moves a whole command by (dx, dy).</summary>
    public static void Translate(DrawCommand c, int dx, int dy)
    {
        c.X += dx;
        c.Y += dy;
        if (IsTwoCorner(c.Op)) { c.X2 += dx; c.Y2 += dy; }
        for (int i = 0; i + 1 < c.Points.Count; i += 2) { c.Points[i] += dx; c.Points[i + 1] += dy; }
    }

    /// <summary>The ink in effect when command <paramref name="index"/> runs.</summary>
    public static int InkAt(Picture picture, int index)
    {
        int ink = picture.InitialInk;
        for (int i = 0; i < index && i < picture.Commands.Count; i++)
            if (picture.Commands[i].Op == DrawOp.SetInk) ink = picture.Commands[i].Color;
        return ink;
    }

    /// <summary>
    /// Makes command <paramref name="index"/> draw in <paramref name="colour"/> without changing any other command:
    /// inserts SetInk before it and restores the previous ink after it where needed. Attribute blocks change their
    /// own ink. Returns the command's new index.
    /// </summary>
    public static int Recolour(Picture picture, int index, int colour)
    {
        var c = picture.Commands[index];
        if (c.Op == DrawOp.AttributeBlock) { c.Color = colour; return index; }
        if (!UsesInk(c.Op) || InkAt(picture, index) == colour) return index;
        int previous = InkAt(picture, index);
        // Restore the old ink afterwards if a later command relies on it.
        bool laterNeedsOld = false;
        for (int i = index + 1; i < picture.Commands.Count; i++)
        {
            if (picture.Commands[i].Op == DrawOp.SetInk) break;
            if (UsesInk(picture.Commands[i].Op)) { laterNeedsOld = true; break; }
        }
        if (laterNeedsOld) picture.Commands.Insert(index + 1, new DrawCommand { Op = DrawOp.SetInk, Color = previous });
        // Reuse a SetInk directly in front if nothing else depends on it.
        if (index > 0 && picture.Commands[index - 1].Op == DrawOp.SetInk)
        {
            picture.Commands[index - 1].Color = colour;
            return index;
        }
        picture.Commands.Insert(index, new DrawCommand { Op = DrawOp.SetInk, Color = colour });
        return index + 1;
    }

    /// <summary>A short human description of a command for the inspector.</summary>
    public static string Describe(DrawOp op) => op switch
    {
        DrawOp.FilledRectangle => "Filled rectangle",
        DrawOp.FilledEllipse => "Filled ellipse",
        DrawOp.FilledPolygon => "Filled shape",
        DrawOp.Freehand => "Pen stroke",
        DrawOp.Shade => "Pattern fill",
        DrawOp.Fill => "Flood fill",
        DrawOp.AttributeBlock => "Attribute block",
        DrawOp.Call => "Sub-picture",
        DrawOp.Image => "Image",
        DrawOp.SetInk => "Set ink",
        DrawOp.SetPaper => "Set paper",
        DrawOp.SetBright => "Set bright",
        DrawOp.Clear => "Clear (background)",
        _ => op.ToString(),
    };

    private static double Sq(double v) => v * v;

    private static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double len = dx * dx + dy * dy;
        double tt = len == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len, 0, 1);
        return Math.Sqrt(Sq(px - (ax + tt * dx)) + Sq(py - (ay + tt * dy)));
    }

    private static bool InsidePolygon(List<int> p, double x, double y)
    {
        bool inside = false;
        int n = p.Count / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = p[i * 2], yi = p[i * 2 + 1], xj = p[j * 2], yj = p[j * 2 + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }
}
