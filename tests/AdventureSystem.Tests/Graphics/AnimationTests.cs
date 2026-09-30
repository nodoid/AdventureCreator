using AdventureSystem.Core.Graphics;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Packaging;

namespace AdventureSystem.Tests.Graphics;

public class AnimationTests
{
    private static Picture Scene()
    {
        var p = new Picture { Width = 64, Height = 32, InitialInk = 0, InitialPaper = 7 };
        p.Commands.Add(new DrawCommand { Op = DrawOp.Clear, Color = 7 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 2 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.FilledRectangle, X = 2, Y = 2, X2 = 5, Y2 = 5, Layer = "lamp" });
        p.Commands.Add(new DrawCommand { Op = DrawOp.FilledRectangle, X = 20, Y = 2, X2 = 23, Y2 = 5 });
        return p;
    }

    [Fact]
    public void BlinkHidesTheLayerForPartOfEachPeriod()
    {
        var p = Scene();
        p.Animations.Add(new PictureAnimation { Kind = AnimationKind.Blink, Layer = "lamp", PeriodMs = 1000, OnPercent = 50 });
        Assert.Equal(p.Palette[2], PictureRenderer.Render(PictureAnimator.Frame(p, 100))[3, 3]);
        Assert.Equal(p.Palette[7], PictureRenderer.Render(PictureAnimator.Frame(p, 700))[3, 3]);
        Assert.Equal(p.Palette[2], PictureRenderer.Render(PictureAnimator.Frame(p, 700))[21, 3]);   // other shapes stay
        Assert.Equal(4, p.Commands.Count);   // the original is untouched
    }

    [Fact]
    public void MoveGoesThereAndBack()
    {
        var a = new PictureAnimation { Kind = AnimationKind.Move, Layer = "lamp", PeriodMs = 1000, Dx = 10, Dy = -4 };
        Assert.Equal((0, 0), PictureAnimator.MoveOffset(a, 0));
        Assert.Equal((5, -2), PictureAnimator.MoveOffset(a, 500));
        Assert.Equal((10, -4), PictureAnimator.MoveOffset(a, 1000));
        Assert.Equal((5, -2), PictureAnimator.MoveOffset(a, 1500));
        a.PingPong = false;
        Assert.Equal((0, 0), PictureAnimator.MoveOffset(a, 1000));

        var p = Scene();
        p.Animations.Add(new PictureAnimation { Kind = AnimationKind.Move, Layer = "lamp", PeriodMs = 1000, Dx = 10 });
        var frame = PictureAnimator.Frame(p, 1000);
        Assert.Equal(12, frame.Commands[2].X);
        Assert.Equal(2, p.Commands[2].X);
    }

    [Fact]
    public void ColourCycleRecoloursOnlyTheLayer()
    {
        var p = Scene();
        p.Animations.Add(new PictureAnimation { Kind = AnimationKind.ColourCycle, Layer = "lamp", PeriodMs = 200, Colours = new() { 4, 6 } });
        var at0 = PictureRenderer.Render(PictureAnimator.Frame(p, 0));
        var at1 = PictureRenderer.Render(PictureAnimator.Frame(p, 250));
        Assert.Equal(p.Palette[4], at0[3, 3]);
        Assert.Equal(p.Palette[6], at1[3, 3]);
        Assert.Equal(p.Palette[2], at1[21, 3]);
    }

    [Fact]
    public void FlipbookSwitchesSubPictures()
    {
        var adventure = new Adventure();
        var red = new Picture { Id = "red", Width = 4, Height = 4, IsSubroutine = true };
        red.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 2 });
        red.Commands.Add(new DrawCommand { Op = DrawOp.FilledRectangle, X = 0, Y = 0, X2 = 3, Y2 = 3 });
        var green = new Picture { Id = "green", Width = 4, Height = 4, IsSubroutine = true };
        green.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 4 });
        green.Commands.Add(new DrawCommand { Op = DrawOp.FilledRectangle, X = 0, Y = 0, X2 = 3, Y2 = 3 });
        var main = new Picture { Id = "main", Width = 16, Height = 16 };
        main.Commands.Add(new DrawCommand { Op = DrawOp.Call, SubPictureId = "red", X = 4, Y = 4, Layer = "flag" });
        main.Animations.Add(new PictureAnimation { Kind = AnimationKind.Flipbook, Layer = "flag", PeriodMs = 100, Frames = new() { "red", "green" } });
        adventure.Pictures.AddRange(new[] { red, green, main });

        Assert.Equal(main.Palette[2], PictureRenderer.Render(PictureAnimator.Frame(main, 50), adventure)[5, 5]);
        Assert.Equal(main.Palette[4], PictureRenderer.Render(PictureAnimator.Frame(main, 150), adventure)[5, 5]);
    }

    [Fact]
    public void AnimationsAndLayersSurviveSaving()
    {
        var adventure = new Adventure();
        var p = Scene();
        p.Id = "scene";
        p.RenderMode = PictureRenderMode.Smooth;
        p.LineWidth = 1.5;
        p.Animations.Add(new PictureAnimation { Kind = AnimationKind.ColourCycle, Layer = "lamp", Colours = new() { 1, 2, 3 } });
        adventure.Pictures.Add(p);
        var bytes = AdventurePackage.SaveToBytes(adventure);
        var loaded = AdventurePackage.Load(bytes).FindPicture("scene")!;
        Assert.Equal(PictureRenderMode.Smooth, loaded.RenderMode);
        Assert.Equal(1.5, loaded.LineWidth);
        Assert.Equal("lamp", loaded.Commands[2].Layer);
        Assert.Equal(new List<int> { 1, 2, 3 }, loaded.Animations.Single().Colours);
    }

    [Fact]
    public void TraceFillsFindsTheRegionInsideAnOutline()
    {
        var p = new Picture { Width = 32, Height = 32, InitialInk = 0, InitialPaper = 7, RenderMode = PictureRenderMode.Smooth };
        p.Commands.Add(new DrawCommand { Op = DrawOp.Rectangle, X = 4, Y = 4, X2 = 20, Y2 = 20 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 2 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Fill, X = 10, Y = 10 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Shade, X = 28, Y = 28, Pattern = new byte[] { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 } });

        var regions = PictureRenderer.TraceFills(p, supersample: 4);
        Assert.Equal(2, regions.Count);
        var inside = regions[0];
        Assert.Equal(2, inside.Ink);
        Assert.Empty(inside.PaperRuns);
        // Inside the box (grid row for picture y = 10), and nothing outside it.
        Assert.Contains(inside.InkRuns, r => r.Y == 42 && r.X0 <= 42 && r.X1 >= 42);
        Assert.DoesNotContain(inside.InkRuns, r => r.X1 > 21 * 4 + FillRegion.Overlap || r.Y > 21 * 4 + FillRegion.Overlap);
        // The pattern fill outside the box has both ink and paper runs.
        Assert.NotEmpty(regions[1].InkRuns);
        Assert.NotEmpty(regions[1].PaperRuns);
    }
}
