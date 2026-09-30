using AdventureSystem.Core.Graphics;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Samples;

namespace AdventureSystem.Tests.Graphics;

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
    public void Spectrum_drawing_gives_the_cell_the_current_paper_as_well()
    {
        // As the ROM does: a plot stamps paper and ink onto its cell (PAWS shades a frame in black on yellow this way).
        var p = new Picture { Width = 16, Height = 8, RenderMode = PictureRenderMode.SpectrumAttributes, InitialInk = 7, InitialPaper = 0 };
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 0 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = 6 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 1, Y = 1 });
        var img = PictureRenderer.Render(p);
        Assert.Equal(Palettes.Spectrum[0], img[1, 1]);
        Assert.Equal(Palettes.Spectrum[6], img[4, 4]);     // the rest of the cell is yellow paper now
        Assert.Equal(Palettes.Spectrum[0], img[12, 4]);    // the untouched cell keeps the black paper
    }

    [Fact]
    public void Spectrum_transparent_and_contrast_colours()
    {
        var p = new Picture { Width = 16, Height = 8, RenderMode = PictureRenderMode.SpectrumAttributes, InitialInk = 2, InitialPaper = 7 };
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = 2 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 1, Y = 1 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = DrawCommand.Transparent });
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = 1 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 2, Y = 2 });                 // INK 8: the cell keeps its red ink
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = DrawCommand.Contrast });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 10, Y = 2 });                // INK 9 on blue paper: white
        var img = PictureRenderer.Render(p);
        Assert.Equal(Palettes.Spectrum[2], img[2, 2]);
        Assert.Equal(Palettes.Spectrum[1], img[5, 5]);
        Assert.Equal(Palettes.Spectrum[7], img[10, 2]);
        Assert.Equal(Palettes.Spectrum[1], img[13, 5]);
    }

    [Fact]
    public void Spectrum_bright_colour_indices_draw_bright()
    {
        // GAC writes bright colours as 8-15.
        var p = new Picture { Width = 8, Height = 8, RenderMode = PictureRenderMode.SpectrumAttributes, InitialInk = 0, InitialPaper = 0 };
        p.Commands.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = 8 + 2 });
        p.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 0, Y = 0 });
        Assert.Equal(Palettes.Spectrum[8 + 2], PictureRenderer.Render(p)[4, 4]);
    }

    [Fact]
    public void Pictures_drawn_over_another_are_composed_in_order()
    {
        var a = new Adventure();
        var frame = new Picture { Id = "frame", Width = 16, Height = 8, RenderMode = PictureRenderMode.SpectrumAttributes, InitialInk = 7, InitialPaper = 0 };
        frame.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 0, Y = 0 });
        var room = new Picture { Id = "room", Width = 16, Height = 8, RenderMode = PictureRenderMode.SpectrumAttributes, InitialInk = 2, InitialPaper = 0 };
        room.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = 9, Y = 0 });
        a.Pictures.Add(frame);
        a.Pictures.Add(room);
        Assert.Same(room, PictureLayers.Compose(a, room, PictureLayers.Parse(null)));
        var img = PictureRenderer.Render(PictureLayers.Compose(a, room, PictureLayers.Parse("frame")), a);
        Assert.Equal(Palettes.Spectrum[2], img[9, 0]);     // the room's red pixel
        Assert.Equal(Palettes.Spectrum[7], img[0, 0]);     // the frame's white pixel, in its own ink
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

    [Fact]
    public void SplashShowsPictureAndTitle()
    {
        var game = ExampleAdventures.Lighthouse();
        var img = AdventureSystem.Core.Graphics.SplashRenderer.Render(game, 480);
        Assert.Equal(480, img.Width);
        var accent = 0xFFA0661B;                                   // title colour on a light background
        Assert.Contains(img.Pixels, p => p == accent);            // the title was drawn
        Assert.True(img.Pixels.Distinct().Count() > 8);           // and the picture
        Assert.True(RasterImage.IsPng(img.ToPng()));
        var svg = AdventureSystem.Core.Graphics.SplashRenderer.RenderSvg(game);
        Assert.StartsWith("<svg", svg);
        Assert.Contains("fill=\"#A0661B\"", svg);                // title pixels
        Assert.True(svg.Length < 400_000, $"SVG is {svg.Length} bytes");
    }
}
