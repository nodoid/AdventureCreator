namespace AdventureSystem.Core.Model;

/// <summary>How a <see cref="Picture"/>'s drawing commands are rasterised.</summary>
public enum PictureRenderMode
{
    /// <summary>Every pixel has its own colour.</summary>
    FullColour,
    /// <summary>ZX Spectrum style: 1-bit pixels with ink/paper/bright attributes per 8x8 cell (authentic PAWS/Quill/Illustrator look).</summary>
    SpectrumAttributes,
    /// <summary>
    /// Anti-aliased vector drawing (Maui.Graphics): smooth lines, curves and text at any size. Flood fills are traced
    /// on a fine grid so they meet the smooth outlines.
    /// </summary>
    Smooth,
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
    /// <summary>Width of lines and outlines in picture pixels (Smooth mode).</summary>
    public double LineWidth { get; set; } = 1;
    /// <summary>Simple animations of the picture's layers (see <see cref="DrawCommand.Layer"/>).</summary>
    public List<PictureAnimation> Animations { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasAnimations => Animations.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Layer));
    /// <summary>A copy sharing everything except the command list (used for animation frames and previews).</summary>
    public Picture WithCommands(List<DrawCommand> commands)
    {
        var copy = (Picture)MemberwiseClone();
        copy.Commands = commands;
        return copy;
    }

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
    /// <summary>
    /// Palette index for SetInk/SetPaper/Clear/AttributeBlock ink. In Spectrum pictures SetInk/SetPaper may also be
    /// <see cref="Transparent"/> (drawing leaves the cell's colour alone: INK 8) or <see cref="Contrast"/> (the cell
    /// gets whichever of white or black stands out against its other colour: INK 9).
    /// </summary>
    public int Color { get; set; }

    public const int Transparent = -1, Contrast = -2;
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
    /// <summary>Optional layer name. Animations act on every command in a layer.</summary>
    public string? Layer { get; set; }

    public DrawCommand Clone()
    {
        var c = (DrawCommand)MemberwiseClone();
        c.Points = new List<int>(Points);
        c.Pattern = Pattern?.ToArray();
        return c;
    }

    public override string ToString() => (string.IsNullOrEmpty(Layer) ? "" : $"[{Layer}] ") + Op switch
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

public enum AnimationKind
{
    /// <summary>The layer is shown for <see cref="PictureAnimation.OnPercent"/>% of each period, hidden for the rest.</summary>
    Blink,
    /// <summary>The layer moves by (Dx, Dy) over the period, then back (or jumps back if not PingPong).</summary>
    Move,
    /// <summary>The layer's ink steps through <see cref="PictureAnimation.Colours"/>, one step per period.</summary>
    ColourCycle,
    /// <summary>Sub-picture (Call) commands in the layer show each of <see cref="PictureAnimation.Frames"/> in turn, one per period.</summary>
    Flipbook,
}

/// <summary>A simple looping animation of one layer of a picture.</summary>
public sealed class PictureAnimation
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public AnimationKind Kind { get; set; } = AnimationKind.Blink;
    /// <summary>The layer (<see cref="DrawCommand.Layer"/>) this animation acts on.</summary>
    public string Layer { get; set; } = "";
    /// <summary>Length of one cycle (Blink, Move) or of one step (ColourCycle, Flipbook), in milliseconds.</summary>
    public int PeriodMs { get; set; } = 1000;
    /// <summary>Delay before the animation starts, in milliseconds; also offsets it against other animations.</summary>
    public int DelayMs { get; set; }
    /// <summary>Blink: percentage of each period that the layer is visible.</summary>
    public int OnPercent { get; set; } = 50;
    /// <summary>Move: horizontal distance in picture pixels.</summary>
    public int Dx { get; set; }
    /// <summary>Move: vertical distance in picture pixels.</summary>
    public int Dy { get; set; }
    /// <summary>Move: go there and back again (true) or jump back to the start and repeat (false).</summary>
    public bool PingPong { get; set; } = true;
    /// <summary>ColourCycle: palette indices stepped through.</summary>
    public List<int> Colours { get; set; } = new();
    /// <summary>Flipbook: picture ids shown in turn.</summary>
    public List<string> Frames { get; set; } = new();
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? $"{Kind} {Layer}" : Name;
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
