using AdventureCreator.Core.Model;

namespace AdventureCreator.Importers.Gac;

internal sealed partial class GacConverter
{
    /// <summary>GAC pictures are 256 x 128 pixels on every machine (the top 16 character rows of the Spectrum screen).</summary>
    public const int PictureWidth = 256, PictureHeight = 128;

    /// <summary>SHADE: a one-pixel checkerboard (GAC uses $AA on even and $55 on odd rows, counted bottom-up).</summary>
    private static readonly byte[] ShadePattern = { 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA };
    private static readonly byte[] ClearPattern = new byte[8];

    private void BuildPictures()
    {
        var called = new HashSet<int>();
        var roomPictures = new HashSet<int>(db.Rooms.Values.Select(r => r.Picture));
        int unknownColours = 0;
        foreach (var data in db.Pictures.Values)
        {
            var pic = db.Machine == GacMachine.Spectrum
                ? ConvertSpectrumPicture(data, called, ref unknownColours)
                : ConvertAmstradPicture(data, called);
            adv.Pictures.Add(pic);
        }
        foreach (var p in adv.Pictures)
        {
            int n = int.Parse(p.Id[1..]);
            p.IsSubroutine = called.Contains(n) && !roomPictures.Contains(n);
        }
        foreach (var n in called.Where(n => !db.Pictures.ContainsKey(n)))
            Note($"A picture calls picture {n}, which does not exist.");
        if (unknownColours > 0)
            Note($"{unknownColours} picture colour commands used GAC's \"transparent\" (8) or \"contrast\" (9) values and were left out.");
        if (db.Pictures.Count > 0)
            Note("GAC fills spread up and down the seed's column only (not a true flood fill); pictures were converted to flood fills and may differ slightly.");
    }

    private static int SpecY(int y) => Math.Clamp(175 - y, 0, PictureHeight - 1);

    private static DrawCommand Box(DrawOp op, int x1, int y1, int x2, int y2) => new()
    {
        Op = op, X = Math.Min(x1, x2), Y = Math.Min(y1, y2), X2 = Math.Max(x1, x2), Y2 = Math.Max(y1, y2),
    };

    private static DrawCommand EllipseCommand(int cx, int cy, int px, int py)
    {
        // The first point is the centre; the second gives the radii as its distance from the centre.
        int rx = Math.Abs(px - cx), ry = Math.Abs(py - cy);
        return new DrawCommand { Op = DrawOp.Ellipse, X = cx - rx, Y = cy - ry, X2 = cx + rx, Y2 = cy + ry };
    }

    private static Picture ConvertSpectrumPicture(GacPictureData data, HashSet<int> called, ref int unknownColours)
    {
        var pic = new Picture
        {
            Id = $"p{data.Number}",
            Name = $"Picture {data.Number}",
            Width = PictureWidth,
            Height = PictureHeight,
            RenderMode = PictureRenderMode.SpectrumAttributes,
            Palette = Palettes.Spectrum.ToList(),
            InitialInk = 0,
            InitialPaper = 7,
        };
        bool bright = false;
        foreach (var c in data.Commands)
        {
            var a = c.Args;
            switch (c.Opcode)
            {
                case 0x01: break; // BORDER
                case 0x02: pic.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = a[0], Y = SpecY(a[1]) }); break;
                case 0x03: pic.Commands.Add(EllipseCommand(a[0], 175 - a[1], a[2], 175 - a[3])); break;
                case 0x04: pic.Commands.Add(new DrawCommand { Op = DrawOp.Fill, X = a[0], Y = SpecY(a[1]) }); break;
                case 0x05: pic.Commands.Add(new DrawCommand { Op = DrawOp.Shade, X = a[0], Y = SpecY(a[1]), Pattern = ClearPattern.ToArray() }); break;
                case 0x06: pic.Commands.Add(new DrawCommand { Op = DrawOp.Shade, X = a[0], Y = SpecY(a[1]), Pattern = ShadePattern.ToArray() }); break;
                case 0x07:
                {
                    int n = a[0] | (a[1] << 8);
                    called.Add(n);
                    pic.Commands.Add(new DrawCommand { Op = DrawOp.Call, SubPictureId = $"p{n}", Scale = 8 });
                    break;
                }
                case 0x08: pic.Commands.Add(Box(DrawOp.Rectangle, a[0], SpecY(a[1]), a[2], SpecY(a[3]))); break;
                case 0x09: pic.Commands.Add(new DrawCommand { Op = DrawOp.Line, X = a[0], Y = SpecY(a[1]), X2 = a[2], Y2 = SpecY(a[3]) }); break;
                case 0x10:
                case 0x11:
                    if (a[0] <= 7)
                        pic.Commands.Add(new DrawCommand { Op = c.Opcode == 0x10 ? DrawOp.SetInk : DrawOp.SetPaper, Color = a[0] + (bright ? 8 : 0) });
                    else unknownColours++;
                    break;
                case 0x12:
                    if (a[0] <= 1)
                    {
                        bright = a[0] == 1;
                        pic.Commands.Add(new DrawCommand { Op = DrawOp.SetBright, X = a[0] });
                    }
                    else unknownColours++;
                    break;
                case 0x13: break; // FLASH
            }
        }
        return pic;
    }

    private static Picture ConvertAmstradPicture(GacPictureData data, HashSet<int> called)
    {
        // Four pens; each has a pair of firmware colours (the second is the flashing alternative). Ink bytes were
        // typed as letters A..Z (1..26) and the firmware keeps the low five bits.
        var palette = new List<uint>();
        var inks = data.Inks ?? new byte[8];
        for (int pen = 0; pen < 4; pen++)
            palette.Add(Palettes.AmstradCpc[Math.Min(inks[pen * 2] & 0x1F, Palettes.AmstradCpc.Length - 1)]);
        var pic = new Picture
        {
            Id = $"p{data.Number}",
            Name = $"Picture {data.Number}",
            Width = PictureWidth,
            Height = PictureHeight,
            RenderMode = PictureRenderMode.FullColour,
            Palette = palette,
            InitialInk = 1,
            InitialPaper = 0,
        };
        static int Y(int b) => PictureHeight - 1 - (b & 0x7F);
        int ink = 1, penA = 1, penB = 1;
        foreach (var c in data.Commands)
        {
            var a = c.Args;
            switch (c.Opcode)
            {
                case 0x01: pic.Commands.Add(new DrawCommand { Op = DrawOp.Line, X = a[0], Y = Y(a[1]), X2 = a[2], Y2 = Y(a[3]) }); break;
                case 0x02: pic.Commands.Add(EllipseCommand(a[0], Y(a[1]), a[2], Y(a[3]))); break;
                case 0x03:
                    if (penA == penB)
                    {
                        pic.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = penA });
                        pic.Commands.Add(new DrawCommand { Op = DrawOp.Fill, X = a[0], Y = Y(a[1]) });
                    }
                    else
                    {
                        pic.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = penA });
                        pic.Commands.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = penB });
                        pic.Commands.Add(new DrawCommand { Op = DrawOp.Shade, X = a[0], Y = Y(a[1]), Pattern = ShadePattern.ToArray() });
                        pic.Commands.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = 0 });
                    }
                    pic.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = ink });
                    break;
                case 0x08: pic.Commands.Add(Box(DrawOp.Rectangle, a[0], Y(a[1]), a[2], Y(a[3]))); break;
                case 0x09: penA = a[0] & 3; penB = a[1] & 3; break;
                case 0x0A:
                {
                    int n = a[0] | ((a[1] & 0x7F) << 8);
                    called.Add(n);
                    pic.Commands.Add(new DrawCommand { Op = DrawOp.Call, SubPictureId = $"p{n}", Scale = 8 });
                    break;
                }
                case 0x0B: pic.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = a[0], Y = Y(a[1]) }); break;
                default:
                    ink = c.Opcode & 3;
                    pic.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = ink });
                    break;
            }
        }
        return pic;
    }
}
