using AdventureSystem.Core.Model;

namespace AdventureSystem.Importers.Paws;

/// <summary>
/// Converts PAWS drawstrings into <see cref="DrawCommand"/> lists.
/// <para>
/// Each drawstring command is one byte (low 3 bits = opcode, bit 3 = OVER, bit 4 = INVERSE, bit 6/7 = sign of
/// the x/y offset) followed by its arguments:
/// 0 PLOT x y (with OVER+INVERSE: absolute move); 1 LINE dx dy (with OVER+INVERSE: relative move);
/// 2 FILL dx dy / SHADE dx dy patterns (bit 5; bit 4 = inverse pattern) / BLOCK h w col row (bit 4 without bit 5);
/// 3 GOSUB picture (bits 3-5 = scale in eighths, 0 = 8/8); 4 TEXT char col row (bits 5-7 = character set);
/// 5 PAPER / BRIGHT (bit 7) value in bits 3-6; 6 INK / FLASH (bit 7); 7 END.
/// Fill and shade start at an offset from the pen but do not move it. PAWS y runs upwards from 0 to 175;
/// it is converted to top-down pixel rows (y' = 175 - y).
/// </para>
/// <para>
/// GOSUB draws another picture starting at the current pen position; scale applies only to relative moves,
/// lines, fills and shades. Every picture is encoded as if its pen started at PAWS (0,0), so a GOSUB of a
/// position-independent picture (no PLOT/BLOCK/TEXT) becomes <see cref="DrawOp.Call"/> with X,Y chosen so that
/// "X + x*Scale/8, Y + y*Scale/8" lands at the right place; other GOSUBs are expanded inline, which is exact.
/// </para>
/// </summary>
internal sealed class PawsPictureConverter
{
    private const int MaxDepth = 10;
    private readonly PawsDatabase db;
    private readonly Dictionary<int, PawsPictureData?> pictures = new();
    private readonly Dictionary<int, bool> positionIndependent = new();
    private readonly Dictionary<int, bool> hasGosub = new();

    public HashSet<string> Notes { get; } = new();
    public bool UsesCustomCharset { get; private set; }

    public PawsPictureConverter(PawsDatabase db) => this.db = db;

    private sealed class PenState
    {
        public int X, Y, Ink, Paper, Bright;
        public PenState Clone() => (PenState)MemberwiseClone();
    }

    public PawsPictureData? Get(int n)
    {
        if (n < 0 || n >= db.NumLocations) return null;
        if (!pictures.TryGetValue(n, out var p)) pictures[n] = p = db.Picture(n);
        return p;
    }

    public Picture? Convert(int n)
    {
        var pic = Get(n);
        if (pic == null || pic.IsEmpty) return null;
        var commands = new List<DrawCommand>();
        var st = new PenState { Ink = pic.Ink, Paper = pic.Paper };
        Run(n, commands, st, 8, 0);
        return new Picture
        {
            Id = $"p{n}",
            Name = pic.IsLocationPicture ? $"Location {n}" : $"Subroutine {n}",
            Width = 256,
            Height = 176,
            RenderMode = PictureRenderMode.SpectrumAttributes,
            InitialInk = pic.Ink,
            InitialPaper = pic.Paper,
            IsSubroutine = !pic.IsLocationPicture,
            Commands = commands,
        };
    }

    // ------------------------------------------------------------------ drawstring parsing

    private static int Length(int b) => (b & 7) switch
    {
        0 or 1 => 3,
        2 => (b & 0x30) == 0x10 ? 5 : (b & 0x20) != 0 ? 4 : 3,
        3 => 2,
        4 => 4,
        5 or 6 => 1,
        _ => 1,
    };

    private IEnumerable<int> Ops(PawsPictureData pic)
    {
        int a = pic.Address;
        for (int guard = 0; guard < 8192; guard++)
        {
            int b = PawsDatabase.Peek(pic.Memory, a);
            if ((b & 7) == 7) yield break;
            yield return a;
            a += Length(b);
        }
    }

    private bool IsPositionIndependent(int n, int depth = 0)
    {
        if (positionIndependent.TryGetValue(n, out var r)) return r;
        if (depth > MaxDepth) return false;
        var pic = Get(n);
        r = true;
        if (pic != null)
            foreach (var a in Ops(pic))
            {
                int b = PawsDatabase.Peek(pic.Memory, a), op = b & 7;
                if (op is 0 or 4 || (op == 2 && (b & 0x30) == 0x10)) { r = false; break; }
                if (op == 3)
                {
                    int t = PawsDatabase.Peek(pic.Memory, a + 1);
                    if (t == n || !IsPositionIndependent(t, depth + 1)) { r = false; break; }
                }
            }
        positionIndependent[n] = r;
        return r;
    }

    private bool HasGosub(int n)
    {
        if (hasGosub.TryGetValue(n, out var r)) return r;
        var pic = Get(n);
        r = pic != null && Ops(pic).Any(a => (PawsDatabase.Peek(pic.Memory, a) & 7) == 3);
        hasGosub[n] = r;
        return r;
    }

    // ------------------------------------------------------------------ conversion

    private static int Td(int y) => 175 - y;

    /// <summary>Walks picture n. With a null output it only simulates (to track the pen and colours).</summary>
    private void Run(int n, List<DrawCommand>? output, PenState st, int scale, int depth)
    {
        var pic = Get(n);
        if (pic == null) return;
        var m = pic.Memory;
        int Scaled(int v) => scale == 8 ? v : v * scale / 8;

        foreach (var a in Ops(pic))
        {
            int b = PawsDatabase.Peek(m, a);
            int a1 = PawsDatabase.Peek(m, a + 1), a2 = PawsDatabase.Peek(m, a + 2);
            bool over = (b & 0x08) != 0, inverse = (b & 0x10) != 0;
            int sx = (b & 0x40) != 0 ? -1 : 1, sy = (b & 0x80) != 0 ? -1 : 1;
            switch (b & 7)
            {
                case 0: // PLOT / absolute move
                    st.X = a1;
                    st.Y = a2;
                    if (!(over && inverse))
                        output?.Add(new DrawCommand { Op = DrawOp.Plot, X = st.X, Y = Td(st.Y), Xor = over, Inverse = inverse });
                    break;

                case 1: // LINE / relative move
                {
                    int nx = st.X + Scaled(sx * a1), ny = st.Y + Scaled(sy * a2);
                    if (!(over && inverse))
                        output?.Add(new DrawCommand
                        {
                            Op = DrawOp.Line, X = st.X, Y = Td(st.Y), X2 = nx, Y2 = Td(ny), Xor = over, Inverse = inverse,
                        });
                    st.X = nx;
                    st.Y = ny;
                    break;
                }

                case 2 when (b & 0x30) == 0x10: // BLOCK height-1 width-1 column row
                {
                    int h = a1, w = a2, col = PawsDatabase.Peek(m, a + 3), row = PawsDatabase.Peek(m, a + 4);
                    int bright = st.Bright == 1 ? 8 : 0;
                    output?.Add(new DrawCommand
                    {
                        Op = DrawOp.AttributeBlock,
                        X = col * 8, Y = row * 8, X2 = (col + w + 1) * 8 - 1, Y2 = (row + h + 1) * 8 - 1,
                        Color = st.Ink < 0 ? st.Ink : st.Ink + bright, Color2 = st.Paper < 0 ? st.Paper : st.Paper + bright,
                    });
                    break;
                }

                case 2 when (b & 0x20) != 0: // SHADE
                {
                    int pat = PawsDatabase.Peek(m, a + 3);
                    var p1 = db.Shade(pat & 15);
                    var p2 = db.Shade(pat >> 4);
                    var pattern = new byte[8];
                    for (int i = 0; i < 8; i++)
                        pattern[i] = (byte)((p1[i] | p2[i]) ^ (inverse ? 0xFF : 0));
                    output?.Add(new DrawCommand
                    {
                        Op = DrawOp.Shade, X = st.X + Scaled(sx * a1), Y = Td(st.Y + Scaled(sy * a2)), Pattern = pattern,
                    });
                    break;
                }

                case 2: // FILL
                    output?.Add(new DrawCommand { Op = DrawOp.Fill, X = st.X + Scaled(sx * a1), Y = Td(st.Y + Scaled(sy * a2)) });
                    break;

                case 3: // GOSUB
                    Gosub(n, a1, (b >> 3) & 7, output, st, depth);
                    break;

                case 4: // TEXT char column row
                    Text(output, (b >> 5) & 7, a1, a2, PawsDatabase.Peek(m, a + 3), over, inverse);
                    break;

                case 5: // PAPER or BRIGHT
                {
                    int v = (b >> 3) & 15;
                    if ((b & 0x80) != 0)
                    {
                        if (v <= 1) { st.Bright = v; output?.Add(new DrawCommand { Op = DrawOp.SetBright, X = v }); }
                        else Notes.Add("Picture BRIGHT 8 (transparent) is ignored.");
                    }
                    else
                    {
                        // PAPER 8 leaves each cell's paper as it is; PAPER 9 contrasts with its ink.
                        st.Paper = v < 8 ? v : v == 8 ? DrawCommand.Transparent : DrawCommand.Contrast;
                        output?.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = st.Paper });
                    }
                    break;
                }

                case 6: // INK or FLASH
                {
                    int v = (b >> 3) & 15;
                    if ((b & 0x80) != 0) Notes.Add("Picture FLASH commands are ignored.");
                    else
                    {
                        st.Ink = v < 8 ? v : v == 8 ? DrawCommand.Transparent : DrawCommand.Contrast;
                        output?.Add(new DrawCommand { Op = DrawOp.SetInk, Color = st.Ink });
                    }
                    break;
                }
            }
        }
    }

    private void Gosub(int caller, int target, int sc, List<DrawCommand>? output, PenState st, int depth)
    {
        int scale = sc == 0 ? 8 : sc;
        if (depth >= MaxDepth || target == caller)
        {
            Notes.Add("A picture GOSUB exceeded PAWS's nesting limit (10) and was skipped.");
            return;
        }
        var pic = Get(target);
        if (pic == null || pic.IsEmpty) return;

        if (output != null && IsPositionIndependent(target) && (scale == 8 || !HasGosub(target)))
        {
            output.Add(new DrawCommand
            {
                Op = DrawOp.Call,
                SubPictureId = $"p{target}",
                X = st.X,
                Y = Td(st.Y) - (int)Math.Round(175.0 * scale / 8),
                Scale = scale,
            });
            var before = st.Clone();
            Run(target, null, st, scale, depth + 1);
            // Colour changes made by the subroutine persist in PAWS: restate them after the call.
            if (st.Ink != before.Ink) output.Add(new DrawCommand { Op = DrawOp.SetInk, Color = st.Ink });
            if (st.Paper != before.Paper) output.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = st.Paper });
            if (st.Bright != before.Bright) output.Add(new DrawCommand { Op = DrawOp.SetBright, X = st.Bright });
        }
        else
        {
            if (output != null)
                Notes.Add("Picture GOSUBs of subroutines that use absolute commands (PLOT/BLOCK/TEXT), or scaled nested GOSUBs, were expanded inline for exact positioning.");
            Run(target, output, st, scale, depth + 1);
        }
    }

    private void Text(List<DrawCommand>? output, int set, int ch, int col, int row, bool over, bool inverse)
    {
        if (output == null) return;
        int x = col * 8, y = row * 8;
        var glyph = db.Glyph(set, ch);
        if (glyph == null)
        {
            if (set != 0) Notes.Add($"Picture TEXT uses character set {set}, which is not in the database; the built-in font is used.");
            string s = ch switch { 96 => "£", 127 => "©", >= 32 and < 127 => ((char)ch).ToString(), _ => "?" };
            output.Add(new DrawCommand { Op = DrawOp.Text, X = x, Y = y, Text = s, Scale = 1, Xor = over, Inverse = inverse });
            return;
        }
        if (set != 0 && ch < 128) UsesCustomCharset = true;
        // Draw the glyph from its bitmap as runs of pixels. Normal mode overwrites the whole cell.
        for (int r = 0; r < 8; r++)
        {
            int bits = glyph[r];
            int c = 0;
            while (c < 8)
            {
                bool on = (bits & (0x80 >> c)) != 0;
                int start = c;
                while (c < 8 && ((bits & (0x80 >> c)) != 0) == on) c++;
                if (!on && over) continue; // OVER: only set pixels are XORed in.
                bool paperRun = on == inverse;
                var cmd = new DrawCommand
                {
                    Op = start == c - 1 ? DrawOp.Plot : DrawOp.Line,
                    X = x + start, Y = y + r, X2 = x + c - 1, Y2 = y + r,
                    Xor = over, Inverse = paperRun && !over,
                };
                output.Add(cmd);
            }
        }
    }
}
