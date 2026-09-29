using AdventureCreator.Core.Graphics;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Samples;

namespace AdventureCreator.Tests.Graphics;

public class RendererTests
{
    [Fact]
    public void LineAndFillFullColour()
    {
        var p = new Picture { Width = 32, Height = 32, InitialPaper = 7, InitialInk = 0 };
        p.Commands.Add(new DrawCommand { Op = DrawOp.Clear, Color = 7 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Rectangle, X = 4, Y = 4, X2 = 20, Y2 = 20 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 2 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Fill, X = 10, Y = 10 });
        var img = PictureRenderer.Render(p);
        Assert.Equal(Palettes.Spectrum[0], img[4, 4]);
        Assert.Equal(Palettes.Spectrum[2], img[10, 10]);
        Assert.Equal(Palettes.Spectrum[7], img[25, 25]);
    }

    [Fact]
    public void SpectrumAttributeClash()
    {
        var p = new Picture { Width = 16, Height = 8, RenderMode = PictureRenderMode.SpectrumAttributes, InitialInk = 0, InitialPaper = 7 };
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 2 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 1, Y = 1 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 4 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 3, Y = 3 });
        var img = PictureRenderer.Render(p);
        // Both pixels share one attribute cell, so the earlier red pixel turns green – authentic colour clash.
        Assert.Equal(Palettes.Spectrum[4], img[1, 1]);
        Assert.Equal(Palettes.Spectrum[4], img[3, 3]);
        Assert.Equal(Palettes.Spectrum[7], img[10, 2]);
    }

    [Fact]
    public void ShadePatternAndSubPictureCall()
    {
        var sub = new Picture { Id = "sub", IsSubroutine = true, Commands = { new DrawCommand { Op = DrawOp.FilledRectangle, X = 0, Y = 0, X2 = 3, Y2 = 3 } } };
        var main = new Picture { Id = "main", Width = 64, Height = 64, InitialInk = 1, InitialPaper = 7, Commands = { new DrawCommand { Op = DrawOp.Call, SubPictureId = "sub", X = 10, Y = 10, Scale = 16 } } };
        var adv = new Adventure { Pictures = { sub, main } };
        var img = PictureRenderer.Render(main, adv);
        Assert.Equal(Palettes.Spectrum[1], img[10, 10]);
        Assert.Equal(Palettes.Spectrum[1], img[16, 16]);
        Assert.Equal(Palettes.Spectrum[7], img[20, 20]);
    }

    [Fact]
    public void PngRoundTrip()
    {
        var img = new RasterImage(5, 3, 0xFF112233);
        img[2, 1] = 0x80FF0000;
        var decoded = RasterImage.DecodePng(img.ToPng());
        Assert.Equal(5, decoded.Width);
        Assert.Equal(img.Pixels, decoded.Pixels);
    }

    [Fact]
    public void ExamplePicturesRender()
    {
        var game = ExampleAdventures.Lighthouse();
        foreach (var pic in game.Pictures)
        {
            var png = PictureRenderer.RenderPng(pic, game, scale: 2);
            Assert.True(RasterImage.IsPng(png));
            var img = PictureRenderer.Render(pic, game);
            Assert.True(img.Pixels.Distinct().Count() > 4, pic.Id + " looks blank");
        }
    }

    [Fact]
    public void TextUsesFont()
    {
        var p = new Picture { Width = 16, Height = 8, InitialInk = 0, InitialPaper = 7, Commands = { new DrawCommand { Op = DrawOp.Text, Text = "A", X = 0, Y = 0, Scale = 1 } } };
        var img = PictureRenderer.Render(p);
        Assert.Contains(img.Pixels, px => px == Palettes.Spectrum[0]);
    }
}
