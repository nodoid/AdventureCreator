using AdventureSystem.Core.Graphics;
using AdventureSystem.Core.Model;

namespace AdventureSystem.Tests.Graphics;

public class PictureGeometryTests
{
    private static Picture Sample()
    {
        var p = new Picture { Width = 100, Height = 100, InitialInk = 0 };
        p.Commands.Add(new DrawCommand { Op = DrawOp.Clear, Color = 7 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.FilledRectangle, X = 10, Y = 10, X2 = 40, Y2 = 40 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Line, X = 0, Y = 50, X2 = 99, Y2 = 50 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Rectangle, X = 60, Y = 60, X2 = 90, Y2 = 90 });
        return p;
    }

    [Fact]
    public void HitTestFindsTopmostShape()
    {
        var p = Sample();
        Assert.Equal(1, PictureGeometry.HitTest(p, null, 20, 20));   // inside the filled box
        Assert.Equal(2, PictureGeometry.HitTest(p, null, 30, 51));   // near the line
        Assert.Equal(3, PictureGeometry.HitTest(p, null, 60, 75));   // on the outline's edge
        Assert.Equal(-1, PictureGeometry.HitTest(p, null, 75, 75));  // hollow middle of the outline
        Assert.Equal(-1, PictureGeometry.HitTest(p, null, 5, 5));    // Clear is never picked
    }

    [Fact]
    public void FilledPolygonHitsInsideAndEllipseOutlineOnlyNearEdge()
    {
        var poly = new DrawCommand { Op = DrawOp.FilledPolygon, Points = new() { 0, 0, 20, 0, 10, 20 } };
        Assert.True(PictureGeometry.Hits(poly, null, 10, 5, 1));
        Assert.False(PictureGeometry.Hits(poly, null, 30, 30, 1));

        var ellipse = new DrawCommand { Op = DrawOp.Ellipse, X = 0, Y = 0, X2 = 40, Y2 = 20 };
        Assert.True(PictureGeometry.Hits(ellipse, null, 0, 10, 1.5));
        Assert.False(PictureGeometry.Hits(ellipse, null, 20, 10, 1.5));
    }

    [Fact]
    public void HandlesResizeAndTranslateMoves()
    {
        var rect = new DrawCommand { Op = DrawOp.Rectangle, X = 10, Y = 10, X2 = 20, Y2 = 20 };
        Assert.Equal(3, PictureGeometry.HandleAt(rect, null, 20, 20, 2));
        PictureGeometry.MoveHandle(rect, 3, 30, 35);
        Assert.Equal((10, 10, 30, 35), (rect.X, rect.Y, rect.X2, rect.Y2));
        PictureGeometry.MoveHandle(rect, 0, 5, 6);
        Assert.Equal((5, 6), (rect.X, rect.Y));

        var poly = new DrawCommand { Op = DrawOp.Polygon, Points = new() { 0, 0, 10, 0, 5, 8 } };
        PictureGeometry.MoveHandle(poly, 2, 5, 12);
        Assert.Equal(12, poly.Points[5]);
        PictureGeometry.Translate(poly, 3, 4);
        Assert.Equal(new List<int> { 3, 4, 13, 4, 8, 16 }, poly.Points);

        var text = new DrawCommand { Op = DrawOp.Text, X = 1, Y = 2, Text = "Hi", Scale = 2 };
        Assert.Equal((1, 2, 32, 17), PictureGeometry.Bounds(text)!.Value);
    }

    [Fact]
    public void RecolourOnlyChangesTheSelectedShape()
    {
        var p = Sample();
        int index = PictureGeometry.Recolour(p, 1, 2);
        Assert.Equal(2, index);
        Assert.Equal(2, PictureGeometry.InkAt(p, index));
        // The line and rectangle after it still use the original ink.
        Assert.Equal(0, PictureGeometry.InkAt(p, p.Commands.FindIndex(c => c.Op == DrawOp.Line)));

        // Recolouring again reuses the SetInk in front rather than adding more.
        int count = p.Commands.Count;
        index = PictureGeometry.Recolour(p, index, 4);
        Assert.Equal(count, p.Commands.Count);
        Assert.Equal(4, PictureGeometry.InkAt(p, index));

        // The rendered pixels agree.
        var img = PictureRenderer.Render(p);
        Assert.Equal(p.Palette[4], img[20, 20]);
        Assert.Equal(p.Palette[0], img[70, 50]);
    }
}
