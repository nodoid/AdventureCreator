namespace AdventureCreator.Core.Model;

/// <summary>How a <see cref="Picture"/>'s drawing commands are rasterised.</summary>
public enum PictureRenderMode
{
    /// <summary>Every pixel has its own colour.</summary>
    FullColour,
    /// <summary>ZX Spectrum style: 1-bit pixels with ink/paper/bright attributes per 8x8 cell (authentic PAWS/Quill/Illustrator look).</summary>
    SpectrumAttributes,
}

/// <summary>
/// A picture is either a vector drawing (a list of <see cref="DrawCommand"/>s, as produced by the built-in
/// graphics editor or imported from PAWS / The Illustrator / GAC), or a bitmap asset, or both
/// (the bitmap is drawn first, then the commands on top).
/// </summary>
public sealed class Picture
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Width { get; set; } = 256;
    public int Height { get; set; } = 176;
    public PictureRenderMode RenderMode { get; set; } = PictureRenderMode.FullColour;
    /// <summary>ARGB colours; drawing commands use indices into this palette. Defaults to the 16 Spectrum colours.</summary>
    public List<uint> Palette { get; set; } = Palettes.Spectrum.ToList();
    public int InitialInk { get; set; }
    public int InitialPaper { get; set; } = 7;
    /// <summary>Optional bitmap background (entry in <see cref="Adventure.Assets"/>).</summary>
    public string? BitmapAsset { get; set; }
    public List<DrawCommand> Commands { get; set; } = new();
    /// <summary>Sub-pictures are only drawn when called from another picture (PAWS/Illustrator GOSUB).</summary>
    public bool IsSubroutine { get; set; }
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}

public enum DrawOp
{
    /// <summary>Clear to Color (paper).</summary>
    Clear,
    SetInk,          // Color
    SetPaper,        // Color
    SetBright,       // X = 0/1 (Spectrum attribute mode)
    Plot,            // X,Y
    Line,            // X,Y -> X2,Y2
    Rectangle,       // X,Y,X2,Y2 corners
    FilledRectangle,
    Ellipse,         // bounding box X,Y,X2,Y2
    FilledEllipse,
    Polygon,         // Points
    FilledPolygon,
    /// <summary>Flood fill from X,Y with the current ink.</summary>
    Fill,
    /// <summary>Flood fill from X,Y with an 8x8 pattern (Pattern = 8 bytes, bit set = ink).</summary>
    Shade,
    /// <summary>Set ink/paper attributes of the 8x8 character cells covering X,Y..X2,Y2 (inclusive, pixels).</summary>
    AttributeBlock,
    /// <summary>Draw Text at X,Y (top-left) with the built-in 8x8 font, scaled by Scale.</summary>
    Text,
    /// <summary>Draw another picture (SubPictureId) offset by X,Y and scaled by Scale/8.</summary>
    Call,
    /// <summary>Paste a bitmap asset (Text = asset name) at X,Y with size X2,Y2 (0 = natural size).</summary>
    Image,
    /// <summary>Freehand stroke through Points using the current ink and brush size Scale.</summary>
    Freehand,
}

public sealed class DrawCommand
{
    public DrawOp Op { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    /// <summary>Palette index for SetInk/SetPaper/Clear/AttributeBlock ink.</summary>
    public int Color { get; set; }
    /// <summary>Paper index for AttributeBlock; -1 = unchanged.</summary>
    public int Color2 { get; set; } = -1;
    /// <summary>Flat list of x,y pairs for polygons and freehand strokes.</summary>
    public List<int> Points { get; set; } = new();
    public byte[]? Pattern { get; set; }
    public string? Text { get; set; }
    public string? SubPictureId { get; set; }
    /// <summary>Scale in eighths for Call (8 = 1:1); pixel size for Text; brush size for Freehand.</summary>
    public int Scale { get; set; } = 8;
    /// <summary>When true, lines/plots are drawn in "OVER" (XOR) mode, as on the Spectrum.</summary>
    public bool Xor { get; set; }
    /// <summary>When true the pixels are drawn in paper colour (INVERSE 1).</summary>
    public bool Inverse { get; set; }

    public DrawCommand Clone()
    {
        var c = (DrawCommand)MemberwiseClone();
        c.Points = new List<int>(Points);
        c.Pattern = Pattern?.ToArray();
        return c;
    }

    public override string ToString() => Op switch
    {
        DrawOp.Clear or DrawOp.SetInk or DrawOp.SetPaper => $"{Op} {Color}",
        DrawOp.Plot or DrawOp.Fill or DrawOp.Shade => $"{Op} {X},{Y}",
        DrawOp.Polygon or DrawOp.FilledPolygon or DrawOp.Freehand => $"{Op} ({Points.Count / 2} points)",
        DrawOp.Text => $"Text {X},{Y} \"{Text}\"",
        DrawOp.Call => $"Call {SubPictureId} @{X},{Y} x{Scale}/8",
        DrawOp.Image => $"Image {Text} @{X},{Y}",
        _ => $"{Op} {X},{Y} {X2},{Y2}",
    };
}

public static class Palettes
{
    /// <summary>ZX Spectrum colours 0-7 normal, 8-15 bright.</summary>
    public static readonly uint[] Spectrum =
    {
        0xFF000000, 0xFF0000D7, 0xFFD70000, 0xFFD700D7, 0xFF00D700, 0xFF00D7D7, 0xFFD7D700, 0xFFD7D7D7,
        0xFF000000, 0xFF0000FF, 0xFFFF0000, 0xFFFF00FF, 0xFF00FF00, 0xFF00FFFF, 0xFFFFFF00, 0xFFFFFFFF,
    };

    /// <summary>Amstrad CPC hardware palette (27 colours).</summary>
    public static readonly uint[] AmstradCpc =
    {
        0xFF000000, 0xFF000080, 0xFF0000FF, 0xFF800000, 0xFF800080, 0xFF8000FF, 0xFFFF0000, 0xFFFF0080, 0xFFFF00FF,
        0xFF008000, 0xFF008080, 0xFF0080FF, 0xFF808000, 0xFF808080, 0xFF8080FF, 0xFFFF8000, 0xFFFF8080, 0xFFFF80FF,
        0xFF00FF00, 0xFF00FF80, 0xFF00FFFF, 0xFF80FF00, 0xFF80FF80, 0xFF80FFFF, 0xFFFFFF00, 0xFFFFFF80, 0xFFFFFFFF,
    };

    /// <summary>Commodore 64 palette (16 colours).</summary>
    public static readonly uint[] Commodore64 =
    {
        0xFF000000, 0xFFFFFFFF, 0xFF880000, 0xFFAAFFEE, 0xFFCC44CC, 0xFF00CC55, 0xFF0000AA, 0xFFEEEE77,
        0xFFDD8855, 0xFF664400, 0xFFFF7777, 0xFF333333, 0xFF777777, 0xFFAAFF66, 0xFF0088FF, 0xFFBBBBBB,
    };

    /// <summary>A general purpose 32-colour palette for new pictures (Spectrum 16 + 16 extra shades).</summary>
    public static readonly uint[] Extended = Spectrum.Concat(new uint[]
    {
        0xFF5A3A1E, 0xFF8B5A2B, 0xFFC19A6B, 0xFF2E4A2E, 0xFF4F7942, 0xFF87A96B, 0xFF1B2A49, 0xFF4169E1,
        0xFF87CEEB, 0xFF708090, 0xFFA9A9A9, 0xFF404040, 0xFFFFA500, 0xFFFFC0CB, 0xFF800020, 0xFFF5DEB3,
    }).ToArray();
}
