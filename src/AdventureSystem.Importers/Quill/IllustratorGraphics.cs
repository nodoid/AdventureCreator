using AdventureSystem.Core.Model;

namespace AdventureSystem.Importers.Quill;

/// <summary>
/// Reader/converter for pictures drawn with Gilsoft's The Illustrator (ZX Spectrum).
/// <para>
/// The graphics database grows down from the top of memory; its control block lives at FAB8h:
/// <c>DW picture pointer table, DW flag table, DW end of flag table; DB number of pictures</c>.
/// Each flag byte has bit 7 set for a location picture (clear = subroutine only), bits 0-2 = ink and
/// bits 3-5 = paper for the initial screen colours.
/// </para>
/// <para>
/// Drawstring opcodes (low 3 bits of the first byte; bit 3 = OVER, bit 4 = INVERSE, bits 6/7 = negative x/y
/// for relative commands):
/// 0 PLOT x,y (OVER+INVERSE = absolute move) · 1 LINE dx,dy (OVER+INVERSE = relative move) ·
/// 2 FILL dx,dy / BLOCK h,w,x,y (bit 4) / SHADE dx,dy,pattern (bit 5) / BSHADE (bits 4+5) ·
/// 3 GOSUB picture, scale in bits 3-5 · 4 RPLOT one pixel in direction bits 5-7 ·
/// 5 PAPER/BRIGHT (bit 7) value bits 3-6 · 6 INK/FLASH (bit 7) · 7 END.
/// Coordinates are Spectrum PLOT coordinates (y = 0 at the bottom of a 176 line window).
/// </para>
/// </summary>
internal sealed class IllustratorGraphics
{
    public const int ControlBlock = 0xFAB8;
    public const int ScreenHeight = 176;

    private readonly QuillDatabase db;
    private readonly List<List<GfxOp>> pictures = new();
    private readonly Dictionary<int, bool> positionIndependent = new();

    private IllustratorGraphics(QuillDatabase db) => this.db = db;

    public int Count => pictures.Count;
    public byte[] Flags { get; private set; } = Array.Empty<byte>();
    public List<string> Warnings { get; } = new();

    public bool IsLocationPicture(int n) => (Flags[n] & 0x80) != 0;

    internal enum Kind { Plot, AbsMove, Line, RelMove, Fill, Block, Shade, BShade, Gosub, RPlot, Ink, Paper, Bright, Flash, End }

    internal readonly record struct GfxOp(Kind Kind, byte Code, int A, int B, int C, int D)
    {
        public bool Over => (Code & 0x08) != 0;
        public bool Inverse => (Code & 0x10) != 0;
        public bool NegX => (Code & 0x40) != 0;
        public bool NegY => (Code & 0x80) != 0;
    }

    /// <summary>Returns the graphics database if a plausible one is present (later Spectrum games only).</summary>
    public static IllustratorGraphics? TryLoad(QuillDatabase db)
    {
        if (db.Platform != QuillPlatform.Spectrum || db.IsEarly) return null;
        int ptrs = db.Word(ControlBlock);
        int flags = db.Word(ControlBlock + 2);
        int flagsEnd = db.Word(ControlBlock + 4);
        int count = db[ControlBlock + 6];
        if (count == 0 || count < db.LocationCount) return null;
        if (ptrs < 0x5C00 || flags < 0x5C00 || ptrs + 2 * count > 0x10000 || flags + count > 0x10000) return null;
        if (flagsEnd != flags + count && ptrs + 2 * count > flags) return null;

        var g = new IllustratorGraphics(db) { Flags = new byte[count] };
        for (int n = 0; n < count; n++)
        {
            g.Flags[n] = db[flags + n];
            int addr = db.Word(ptrs + 2 * n);
            if (addr < 0x5C00) return null;
            var ops = Decode(db, addr);
            if (ops is null) return null;
            g.pictures.Add(ops);
        }
        return g;
    }

    private static List<GfxOp>? Decode(QuillDatabase db, int address)
    {
        var ops = new List<GfxOp>();
        int a = address;
        for (int guard = 0; guard < 8192; guard++)
        {
            if (a > 0xFFFF) return null;
            byte c = db[a];
            int Arg(int i) => db[a + i];
            GfxOp op;
            int len;
            bool overInverse = (c & 0x18) == 0x18;
            int value = (c >> 3) & 0x0F;
            switch (c & 7)
            {
                case 0: op = new GfxOp(overInverse ? Kind.AbsMove : Kind.Plot, c, Arg(1), Arg(2), 0, 0); len = 3; break;
                case 1: op = new GfxOp(overInverse ? Kind.RelMove : Kind.Line, c, Arg(1), Arg(2), 0, 0); len = 3; break;
                case 2:
                    switch (c & 0x30)
                    {
                        case 0x00: op = new GfxOp(Kind.Fill, c, Arg(1), Arg(2), 0, 0); len = 3; break;
                        case 0x10: op = new GfxOp(Kind.Block, c, Arg(1), Arg(2), Arg(3), Arg(4)); len = 5; break;
                        case 0x20: op = new GfxOp(Kind.Shade, c, Arg(1), Arg(2), Arg(3), 0); len = 4; break;
                        default: op = new GfxOp(Kind.BShade, c, Arg(1), Arg(2), Arg(3), 0); len = 4; break;
                    }
                    break;
                case 3: op = new GfxOp(Kind.Gosub, c, Arg(1), (c >> 3) & 7, 0, 0); len = 2; break;
                case 4: op = new GfxOp(Kind.RPlot, c, (c >> 5) & 7, 0, 0, 0); len = 1; break;
                case 5: op = new GfxOp((c & 0x80) != 0 ? Kind.Bright : Kind.Paper, c, value, 0, 0, 0); len = 1; break;
                case 6: op = new GfxOp((c & 0x80) != 0 ? Kind.Flash : Kind.Ink, c, value, 0, 0, 0); len = 1; break;
                default: op = new GfxOp(Kind.End, c, 0, 0, 0, 0); len = 1; break;
            }
            ops.Add(op);
            if (op.Kind == Kind.End) return ops;
            a += len;
        }
        return null;
    }

    internal IReadOnlyList<GfxOp> Ops(int n) => pictures[n];

    // ---------------------------------------------------------------- conversion

    /// <summary>RPLOT directions (dx, dy) in Spectrum coordinates, indexed by bits 5-7 of the opcode.</summary>
    private static readonly (int Dx, int Dy)[] RPlotMoves =
    {
        (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1),
    };

    /// <summary>True when picture n can be drawn by <see cref="DrawOp.Call"/> in its own coordinate frame (no absolute commands).</summary>
    public bool IsPositionIndependent(int n) => IsPositionIndependent(n, 0);

    private bool IsPositionIndependent(int n, int depth)
    {
        if (positionIndependent.TryGetValue(n, out var r)) return r;
        if (depth > 10) return false;
        bool ok = true;
        foreach (var op in pictures[n])
        {
            if (op.Kind is Kind.Plot or Kind.AbsMove or Kind.Block) { ok = false; break; }
            if (op.Kind == Kind.Gosub && (op.A >= Count || op.A == n || !IsPositionIndependent(op.A, depth + 1))) { ok = false; break; }
        }
        positionIndependent[n] = ok;
        return ok;
    }

    private bool HasGosub(int n) => pictures[n].Any(o => o.Kind == Kind.Gosub);

    /// <summary>Should a GOSUB to <paramref name="callee"/> at <paramref name="scale"/> be emitted as a Call (else it is inlined)?</summary>
    private bool UseCall(int callee, int scale) =>
        !IsLocationPicture(callee) && IsPositionIndependent(callee) && (scale == 0 || !HasGosub(callee));

    /// <summary>Builds the model picture for Illustrator picture <paramref name="n"/>.</summary>
    public Picture ToPicture(int n)
    {
        bool location = IsLocationPicture(n);
        // Subroutine-only pictures that contain no absolute commands are expressed relative to the point they are
        // called from (origin 0,0; y grows downwards), so DrawOp.Call can offset and scale them.
        bool relative = !location && IsPositionIndependent(n);
        var pic = new Picture
        {
            Id = PictureId(n),
            Name = location ? $"Location {n}" : $"Subroutine {n}",
            Width = 256,
            Height = ScreenHeight,
            RenderMode = PictureRenderMode.SpectrumAttributes,
            InitialInk = Flags[n] & 7,
            InitialPaper = (Flags[n] >> 3) & 7,
            IsSubroutine = !location,
        };
        var state = new PenState { Relative = relative, Paper = pic.InitialPaper, Ink = pic.InitialInk };
        Run(n, state, pic.Commands, 0, 0);
        return pic;
    }

    public static string PictureId(int n) => $"p{n}";

    private sealed class PenState
    {
        public int X, Y;
        public bool Relative;
        public int Ink, Paper;
        public PenState Clone() => (PenState)MemberwiseClone();
    }

    private int TopDown(PenState s, int y) => s.Relative ? -y : ScreenHeight - 1 - y;

    private static int Scaled(int v, int scale) => scale == 0 ? v : (v * scale) >> 3;

    private void Move(PenState s, in GfxOp op, int scale, bool wrap)
    {
        int dx = Scaled(op.A, scale), dy = Scaled(op.B, scale);
        s.X += op.NegX ? -dx : dx;
        s.Y += op.NegY ? -dy : dy;
        if (wrap && !s.Relative) WrapAbsolute(s);
    }

    private static void WrapAbsolute(PenState s)
    {
        s.X &= 0xFF;
        s.Y = ((s.Y % ScreenHeight) + ScreenHeight) % ScreenHeight;
    }

    private void Run(int n, PenState s, List<DrawCommand>? output, int depth, int scale)
    {
        foreach (var op in pictures[n])
        {
            switch (op.Kind)
            {
                case Kind.End:
                    return;
                case Kind.AbsMove:
                    s.X = op.A; s.Y = op.B;
                    break;
                case Kind.Plot:
                    s.X = op.A; s.Y = op.B;
                    output?.Add(new DrawCommand { Op = DrawOp.Plot, X = s.X, Y = TopDown(s, s.Y), Xor = op.Over, Inverse = op.Inverse });
                    break;
                case Kind.RelMove:
                    Move(s, op, scale, wrap: true);
                    break;
                case Kind.Line:
                {
                    int x0 = s.X, y0 = s.Y;
                    Move(s, op, scale, wrap: false);
                    output?.Add(new DrawCommand
                    {
                        Op = DrawOp.Line, X = x0, Y = TopDown(s, y0), X2 = s.X, Y2 = TopDown(s, s.Y),
                        Xor = op.Over, Inverse = op.Inverse,
                    });
                    if (!s.Relative) { s.X = Math.Clamp(s.X, 0, 255); s.Y = Math.Clamp(s.Y, 0, ScreenHeight - 1); }
                    break;
                }
                case Kind.Fill:
                    Move(s, op, scale, wrap: true);
                    output?.Add(new DrawCommand { Op = DrawOp.Fill, X = s.X, Y = TopDown(s, s.Y) });
                    break;
                case Kind.Shade:
                case Kind.BShade:
                    Move(s, op, scale, wrap: true);
                    output?.Add(new DrawCommand { Op = DrawOp.Shade, X = s.X, Y = TopDown(s, s.Y), Pattern = ShadePattern(op.C) });
                    if (op.Kind == Kind.BShade)
                        Warn("BSHADE (shade that also overwrites the area's border) was imported as a normal SHADE.");
                    break;
                case Kind.Block:
                {
                    // BLOCK h w x y — character cells, rows counted from the top of the screen.
                    int h = op.A, w = op.B, cx = op.C, cy = op.D;
                    output?.Add(new DrawCommand
                    {
                        Op = DrawOp.AttributeBlock, X = cx * 8, Y = cy * 8, X2 = (cx + w) * 8 - 1, Y2 = (cy + h) * 8 - 1,
                        Color = s.Ink, Color2 = s.Paper,
                    });
                    break;
                }
                case Kind.Gosub:
                {
                    int callee = op.A, subScale = op.B;
                    if (callee >= Count || depth >= 10)
                    {
                        Warn($"Picture {n}: GOSUB {callee} ignored (missing picture or nesting too deep).");
                        break;
                    }
                    if (UseCall(callee, subScale))
                    {
                        output?.Add(new DrawCommand
                        {
                            Op = DrawOp.Call, SubPictureId = PictureId(callee), X = s.X, Y = TopDown(s, s.Y),
                            Scale = subScale == 0 ? 8 : subScale,
                        });
                        // The drawing cursor carries on from wherever the subroutine left it.
                        var sim = new PenState { Relative = true, Ink = s.Ink, Paper = s.Paper };
                        Run(callee, sim, null, depth + 1, subScale);
                        s.X += sim.X; s.Y += sim.Y;
                        s.Ink = sim.Ink; s.Paper = sim.Paper;
                        if (!s.Relative) WrapAbsolute(s);
                    }
                    else
                    {
                        Run(callee, s, output, depth + 1, subScale);
                    }
                    break;
                }
                case Kind.RPlot:
                {
                    var (dx, dy) = RPlotMoves[op.A];
                    s.X += dx; s.Y += dy;
                    if (!s.Relative) WrapAbsolute(s);
                    output?.Add(new DrawCommand { Op = DrawOp.Plot, X = s.X, Y = TopDown(s, s.Y), Xor = op.Over, Inverse = op.Inverse });
                    break;
                }
                case Kind.Ink:
                {
                    int c = Colour(op.A, s.Paper);
                    if (c >= 0) { s.Ink = c; output?.Add(new DrawCommand { Op = DrawOp.SetInk, Color = c }); }
                    break;
                }
                case Kind.Paper:
                {
                    int c = Colour(op.A, s.Ink);
                    if (c >= 0) { s.Paper = c; output?.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = c }); }
                    break;
                }
                case Kind.Bright:
                    if (op.A <= 1) output?.Add(new DrawCommand { Op = DrawOp.SetBright, X = op.A });
                    break;
                case Kind.Flash:
                    if (op.A == 1) Warn("FLASH in pictures is not supported and was ignored.");
                    break;
            }
        }
    }

    /// <summary>Spectrum colour argument: 0-7, 8 = transparent (-1, unchanged), 9 = contrast with the other colour.</summary>
    private static int Colour(int v, int other) => v switch
    {
        <= 7 => v,
        9 => other < 4 ? 7 : 0,
        _ => -1,
    };

    /// <summary>
    /// SHADE's pattern byte is a 4x2 pixel tile: bit ((x&amp;1)&lt;&lt;2 | (x&amp;2) | (y&amp;1)) is the pixel at
    /// Spectrum coordinates (x, y). Expanded here into an 8x8 pattern for top-down rows (row = y' mod 8,
    /// bit 7 = leftmost pixel), anchored to the screen origin.
    /// </summary>
    public static byte[] ShadePattern(int f)
    {
        var p = new byte[8];
        for (int row = 0; row < 8; row++)
        {
            int yb = (ScreenHeight - 1 - row) & 1;
            int b = 0;
            for (int col = 0; col < 8; col++)
            {
                int k = ((col & 1) << 2) | (col & 2) | yb;
                if (((f >> k) & 1) != 0) b |= 0x80 >> col;
            }
            p[row] = (byte)b;
        }
        return p;
    }

    private void Warn(string message)
    {
        if (!Warnings.Contains(message)) Warnings.Add(message);
    }
}
