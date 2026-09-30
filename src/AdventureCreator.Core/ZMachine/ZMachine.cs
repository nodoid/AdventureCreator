using System.Text;

namespace AdventureCreator.Core.ZMachine;

/// <summary>What the Z-machine is waiting for, or why it stopped.</summary>
public enum ZStop { NeedLine, NeedChar, Quit }

/// <summary>
/// A Z-machine interpreter (Infocom / Inform story files, versions 1–5, 7 and 8) following the Z-Machine Standards
/// Document 1.1. It runs until the story wants input, collecting the text it prints. The upper window is kept as a
/// character grid so its first line can be shown as a status bar. Version 6 runs with a virtual 640×400 screen:
/// its extra windows feed the status bar and draw_picture reports the pictures drawn (from a Blorb file), which
/// hosts show in their picture area.
/// </summary>
public sealed partial class ZMachine
{
    private readonly byte[] story;
    private byte[] mem;
    public int Version { get; }
    private readonly int staticBase;
    private int pc;
    private readonly List<ushort> stack = new();
    private readonly List<Frame> frames = new();
    private Random random;
    private readonly int? fixedSeed;

    // Output
    private readonly StringBuilder lower = new();
    private readonly List<(int Address, int Count)> stream3 = new();
    private bool screenOutput = true;
    private int window;                      // 0 = lower, 1 = upper
    private int upperLines;
    private char[][] upperGrid = Array.Empty<char[]>();
    private int cursorRow, cursorCol;
    private const int ScreenWidth = 80;

    // Pending input (the machine is paused on read / read_char)
    private PendingRead? pendingRead;

    // Undo: several levels, newest last.
    private readonly List<Snapshot> undo = new();
    private const int UndoLevels = 10;

    /// <summary>Raised requests the host deals with after <see cref="Run"/> returns.</summary>
    public bool SaveRequested { get; private set; }
    public bool RestoreRequested { get; private set; }
    public bool ClearRequested { get; private set; }
    public bool BeepRequested { get; private set; }

    private sealed class Frame
    {
        public int ReturnPc;
        public int StoreVar = -1;           // -1 = discard the result
        public ushort[] Locals = Array.Empty<ushort>();
        public int ArgCount;
        public int StackBase;
    }

    private sealed record PendingRead(bool Char, int TextBuffer, int ParseBuffer, int StoreVar);

    /// <summary>Picture sizes (V6), by picture number, for picture_data.</summary>
    private readonly IReadOnlyDictionary<int, (int Width, int Height)> pictureSizes;

    /// <summary>Pictures drawn with draw_picture (V6) during the last <see cref="Run"/>, in order.</summary>
    public List<int> PicturesDrawn { get; } = new();

    public ZMachine(byte[] story, int? seed = null, IReadOnlyDictionary<int, (int Width, int Height)>? pictures = null)
    {
        pictureSizes = pictures ?? new Dictionary<int, (int, int)>();
        if (story.Length < 64) throw new InvalidDataException("The file is too small to be a Z-code story.");
        Version = story[0];
        if (Version is < 1 or > 8) throw new InvalidDataException($"Unknown Z-machine version {Version}.");
        this.story = story.ToArray();
        mem = this.story.ToArray();
        staticBase = RW(0x0E);
        fixedSeed = seed;
        random = seed.HasValue ? new Random(seed.Value) : new Random();
        Reset();
    }

    public int Release => RW(0x02);
    public string Serial => Encoding.ASCII.GetString(story, 0x12, 6);
    public int Checksum => RW(0x1C);

    /// <summary>The first line of the upper window (V4+ status bars), or the V3 status line.</summary>
    public string StatusLine { get; private set; } = "";
    /// <summary>V3 status line parts (location, score or hours, turns or minutes, whether it's a time game).</summary>
    public (string Location, int Score, int Turns, bool Time)? Status3 { get; private set; }

    // ================================================================= running

    /// <summary>Starts (or restarts) the story.</summary>
    public void Reset()
    {
        int flags2 = mem.Length > 0x11 ? RW(0x10) & 3 : 0;   // transcript and fixed-pitch bits survive a restart
        mem = story.ToArray();
        WW(0x10, (ushort)((RW(0x10) & ~3) | flags2));
        stack.Clear();
        frames.Clear();
        frames.Add(new Frame { ReturnPc = -1 });
        if (Version == 6)
        {
            // V6 starts by calling the main routine; returning from it ends the story.
            ResetWindows6();
            pc = -1;
            opCount = 0;
            Call(RW(0x06), 0, 0, -1);
        }
        else pc = RW(0x06);
        upperLines = 0;
        upperGrid = Array.Empty<char[]>();
        window = 0;
        pendingRead = null;
        stream3.Clear();
        screenOutput = true;
        SetHeader();
    }

    private void SetHeader()
    {
        if (Version <= 3)
        {
            mem[1] = (byte)((mem[1] & ~0x10) | 0x20);   // status line available, screen splitting available
        }
        else
        {
            mem[1] = (byte)(mem[1] & ~0x81 | 0x1C);      // bold, italic, fixed-space; no colours, no timed input
            mem[0x1E] = 6;                               // interpreter number (IBM PC)
            mem[0x1F] = (byte)'A';
            mem[0x20] = 255;                             // screen height (lines): unlimited
            mem[0x21] = ScreenWidth;
        }
        if (Version == 6)
        {
            mem[1] = (byte)((mem[1] & ~0x81) | 0x1E | (pictureSizes.Count > 0 ? 0x02 : 0));   // pictures, bold, italic, fixed
            mem[0x20] = ScreenUnitsHigh / FontHigh;
            mem[0x21] = ScreenUnitsWide / FontWide;
            WW(0x22, ScreenUnitsWide);
            WW(0x24, ScreenUnitsHigh);
            mem[0x26] = FontHigh;    // V6 swaps these two
            mem[0x27] = FontWide;
            mem[0x2C] = 1;
            mem[0x2D] = 1;
            int clear = (1 << 5) | (1 << 7) | (1 << 6) | (pictureSizes.Count > 0 ? 0 : 1 << 3);
            WW(0x10, (ushort)(RW(0x10) & ~clear));
        }
        else if (Version >= 5)
        {
            WW(0x22, ScreenWidth);
            WW(0x24, 255);
            mem[0x26] = 1;
            mem[0x27] = 1;
            mem[0x2C] = 1;
            mem[0x2D] = 1;
            // Clear requests we can't honour: pictures, mouse, sound. Undo and colours-off are fine.
            WW(0x10, (ushort)(RW(0x10) & ~((1 << 3) | (1 << 5) | (1 << 7) | (1 << 6))));
        }
        mem[0x32] = 1;
        mem[0x33] = 1;
    }

    /// <summary>
    /// Runs until the story wants input or quits. <paramref name="input"/> answers the pending read (ignored on the
    /// first call). Returns why it stopped; <see cref="TakeOutput"/> gives the text printed.
    /// </summary>
    public ZStop Run(string? input)
    {
        SaveRequested = RestoreRequested = ClearRequested = BeepRequested = false;
        PicturesDrawn.Clear();
        if (pendingRead != null && input != null) CompleteRead(input);
        if (pendingRead != null) return pendingRead.Char ? ZStop.NeedChar : ZStop.NeedLine;
        long budget = 50_000_000;
        while (budget-- > 0)
        {
            if (quit) return ZStop.Quit;
            Step();
            if (pendingRead != null)
            {
                UpdateStatus();
                return pendingRead.Char ? ZStop.NeedChar : ZStop.NeedLine;
            }
        }
        throw new InvalidOperationException("The story ran for too long without asking for input.");
    }

    private bool quit;
    public bool HasQuit => quit;

    /// <summary>The text printed to the main window since the last call.</summary>
    public string TakeOutput()
    {
        var s = lower.ToString();
        lower.Clear();
        return s;
    }

    // ================================================================= memory

    private int RB(int a) => mem[a & 0x7FFFF];
    private int RW(int a) => (mem[a] << 8) | mem[a + 1];
    private void WB(int a, int v)
    {
        if (a >= staticBase) throw new ZException($"Write to static memory at {a:X}.");
        mem[a] = (byte)v;
    }
    private void WW(int a, int v)
    {
        mem[a] = (byte)(v >> 8);
        mem[a + 1] = (byte)v;
    }
    private void WWChecked(int a, int v)
    {
        if (a + 1 >= staticBase) throw new ZException($"Write to static memory at {a:X}.");
        WW(a, v);
    }

    private int Unpack(int packed, bool routine) => Version switch
    {
        <= 3 => packed * 2,
        <= 5 => packed * 4,
        6 or 7 => packed * 4 + 8 * RW(routine ? 0x28 : 0x2A),
        _ => packed * 8,
    };

    // ================================================================= variables

    private int ReadVar(int v)
    {
        if (v == 0)
        {
            if (stack.Count <= Top.StackBase) throw new ZException("Stack underflow.");
            var x = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return x;
        }
        if (v < 16) return Top.Locals[v - 1];
        return RW(Globals + 2 * (v - 16));
    }

    private void WriteVar(int v, int value)
    {
        value &= 0xFFFF;
        if (v == 0) stack.Add((ushort)value);
        else if (v < 16) Top.Locals[v - 1] = (ushort)value;
        else WW(Globals + 2 * (v - 16), value);
    }

    /// <summary>Variable access by reference (inc, dec, load, store, pull): the stack is read or replaced in place.</summary>
    private int ReadVarRef(int v)
    {
        if (v == 0) return stack.Count > Top.StackBase ? stack[^1] : throw new ZException("Stack underflow.");
        return ReadVar(v);
    }

    private void WriteVarRef(int v, int value)
    {
        if (v == 0)
        {
            if (stack.Count > Top.StackBase) stack[^1] = (ushort)value;
            else stack.Add((ushort)value);
        }
        else WriteVar(v, value);
    }

    private Frame Top => frames[^1];
    private int Globals => RW(0x0C);

    // ================================================================= instruction decoding

    private readonly int[] ops = new int[8];
    private int opCount;

    private int Fetch(int type) => type switch
    {
        0 => (RB(pc++) << 8) | RB(pc++),
        1 => RB(pc++),
        2 => ReadVar(RB(pc++)),
        _ => 0,
    };

    private void ReadTypes(int typeByte)
    {
        for (int shift = 6; shift >= 0; shift -= 2)
        {
            int t = (typeByte >> shift) & 3;
            if (t == 3) break;
            ops[opCount++] = Fetch(t);
        }
    }

    private void Step()
    {
        int start = pc;
        int op = RB(pc++);
        opCount = 0;
        try
        {
            if (op == 0xBE && Version >= 5)
            {
                int ext = RB(pc++);
                int types = RB(pc++);
                ReadTypesThen(types);
                Ext(ext);
            }
            else if (op >= 0xC0)
            {
                int num = op & 0x1F;
                if (op >= 0xE0 && (num == 12 || num == 26))
                {
                    int t1 = RB(pc++), t2 = RB(pc++);
                    ReadTypesThen(t1);
                    if (opCount == 4) ReadTypesThen(t2);
                }
                else ReadTypesThen(RB(pc++));
                if (op < 0xE0) TwoOp(num);
                else Var(num);
            }
            else if (op >= 0x80)
            {
                int type = (op >> 4) & 3;
                if (type == 3) ZeroOp(op & 0x0F);
                else
                {
                    ops[opCount++] = Fetch(type);
                    OneOp(op & 0x0F);
                }
            }
            else
            {
                ops[opCount++] = Fetch((op & 0x40) != 0 ? 2 : 1);
                ops[opCount++] = Fetch((op & 0x20) != 0 ? 2 : 1);
                TwoOp(op & 0x1F);
            }
        }
        catch (ZException ex)
        {
            throw new ZException($"{ex.Message} (instruction {op:X2} at {start:X5})");
        }
    }

    private void ReadTypesThen(int typeByte) => ReadTypes(typeByte);

    // ================================================================= store and branch

    private void Store(int value) => WriteVar(RB(pc++), value);

    private void Branch(bool condition)
    {
        int b = RB(pc++);
        bool onTrue = (b & 0x80) != 0;
        int offset;
        if ((b & 0x40) != 0) offset = b & 0x3F;
        else
        {
            offset = ((b & 0x3F) << 8) | RB(pc++);
            if ((offset & 0x2000) != 0) offset -= 0x4000;
        }
        if (condition != onTrue) return;
        if (offset == 0) Return(0);
        else if (offset == 1) Return(1);
        else pc += offset - 2;
    }

    private static short S(int v) => (short)v;

    // ================================================================= calls

    private void Call(int packed, int argStart, int argCount, int storeVar)
    {
        if (packed == 0)
        {
            if (storeVar >= 0) WriteVar(storeVar, 0);
            return;
        }
        int addr = Unpack(packed, routine: true);
        int locals = RB(addr++);
        if (locals > 15) throw new ZException($"Bad routine at {addr - 1:X}.");
        var frame = new Frame { ReturnPc = pc, StoreVar = storeVar, Locals = new ushort[locals], ArgCount = argCount, StackBase = stack.Count };
        for (int i = 0; i < locals; i++)
        {
            if (Version <= 4) { frame.Locals[i] = (ushort)RW(addr); addr += 2; }
        }
        for (int i = 0; i < argCount && i < locals; i++) frame.Locals[i] = (ushort)ops[argStart + i];
        frames.Add(frame);
        pc = addr;
    }

    private void Return(int value)
    {
        if (frames.Count <= 1) throw new ZException("Return from the main routine.");
        if (frames.Count == 2 && Version == 6 && frames[1].ReturnPc < 0) { frames.RemoveAt(1); stack.Clear(); quit = true; return; }
        var f = frames[^1];
        frames.RemoveAt(frames.Count - 1);
        stack.RemoveRange(f.StackBase, stack.Count - f.StackBase);
        pc = f.ReturnPc;
        if (pc < 0) { quit = true; return; }   // V6: the main routine returned
        if (f.StoreVar >= 0) WriteVar(f.StoreVar, value);
    }

    private void CallStore(int start) => Call(ops[start], start + 1, opCount - start - 1, RB(pc++));
    private void CallDiscard(int start) => Call(ops[start], start + 1, opCount - start - 1, -1);

    // ================================================================= opcodes

    private void TwoOp(int n)
    {
        int a = opCount > 0 ? ops[0] : 0, b = opCount > 1 ? ops[1] : 0;
        switch (n)
        {
            case 1: // je
            {
                bool eq = false;
                for (int i = 1; i < opCount; i++) if (ops[i] == a) eq = true;
                Branch(eq);
                break;
            }
            case 2: Branch(S(a) < S(b)); break;
            case 3: Branch(S(a) > S(b)); break;
            // The comparison uses the wrapped 16-bit value ($7FFF + 1 = -32768).
            case 4: { int v = S(ReadVarRef(a) - 1); WriteVarRef(a, v); Branch(v < S(b)); break; }
            case 5: { int v = S(ReadVarRef(a) + 1); WriteVarRef(a, v); Branch(v > S(b)); break; }
            case 6: Branch(Parent(a) == b); break;
            case 7: Branch((a & b) == b); break;
            case 8: Store(a | b); break;
            case 9: Store(a & b); break;
            case 10: Branch(TestAttr(a, b)); break;
            case 11: SetAttr(a, b, true); break;
            case 12: SetAttr(a, b, false); break;
            case 13: WriteVarRef(a, b); break;
            case 14: InsertObj(a, b); break;
            case 15: Store(RW((a + 2 * S(b)) & 0xFFFF)); break;
            case 16: Store(RB((a + S(b)) & 0xFFFF)); break;
            case 17: Store(GetProp(a, b)); break;
            case 18: Store(GetPropAddr(a, b)); break;
            case 19: Store(GetNextProp(a, b)); break;
            case 20: Store(S(a) + S(b)); break;
            case 21: Store(S(a) - S(b)); break;
            case 22: Store(S(a) * S(b)); break;
            case 23: if (S(b) == 0) throw new ZException("Division by zero."); Store(S(a) / S(b)); break;
            case 24: if (S(b) == 0) throw new ZException("Division by zero."); Store(S(a) % S(b)); break;
            case 25: CallStore(0); break;
            case 26: CallDiscard(0); break;
            case 27: break;   // set_colour
            case 28: Throw(a, b); break;
            default: throw new ZException($"Unknown 2OP opcode {n}.");
        }
    }

    private void OneOp(int n)
    {
        int a = ops[0];
        switch (n)
        {
            case 0: Branch(a == 0); break;
            case 1: { int s = Sibling(a); Store(s); Branch(s != 0); break; }
            case 2: { int c = Child(a); Store(c); Branch(c != 0); break; }
            case 3: Store(Parent(a)); break;
            case 4: Store(PropLen(a)); break;
            case 5: WriteVarRef(a, S(ReadVarRef(a)) + 1); break;
            case 6: WriteVarRef(a, S(ReadVarRef(a)) - 1); break;
            case 7: Print(DecodeString(a, out _)); break;
            case 8: CallStore(0); break;
            case 9: RemoveObj(a); break;
            case 10: Print(ObjectName(a)); break;
            case 11: Return(a); break;
            case 12: pc += S(a) - 2; break;
            case 13: Print(DecodeString(Unpack(a, routine: false), out _)); break;
            case 14: Store(ReadVarRef(a)); break;
            case 15:
                if (Version <= 4) Store(~a);
                else CallDiscard(0);
                break;
            default: throw new ZException($"Unknown 1OP opcode {n}.");
        }
    }

    private void ZeroOp(int n)
    {
        switch (n)
        {
            case 0: Return(1); break;
            case 1: Return(0); break;
            case 2: Print(DecodeString(pc, out int len)); pc += len; break;
            case 3: Print(DecodeString(pc, out int len2)); pc += len2; Print("\n"); Return(1); break;
            case 4: break;
            case 5: // save
                SaveRequested = true;
                if (Version <= 3) Branch(true); else Store(1);
                break;
            case 6: // restore: the host restores between turns; the story's own restore reports failure
                RestoreRequested = true;
                if (Version <= 3) Branch(false); else Store(0);
                break;
            case 7: Reset(); break;
            case 8: Return(ReadVar(0)); break;
            case 9:
                if (Version <= 4) ReadVar(0);
                else Store(frames.Count);
                break;
            case 10: quit = true; break;
            case 11: Print("\n"); break;
            case 12: UpdateStatus(); break;
            case 13: Branch(true); break;   // verify
            case 15: Branch(true); break;   // piracy
            default: throw new ZException($"Unknown 0OP opcode {n}.");
        }
    }

    private void Var(int n)
    {
        int a = opCount > 0 ? ops[0] : 0, b = opCount > 1 ? ops[1] : 0, c = opCount > 2 ? ops[2] : 0;
        switch (n)
        {
            case 0: CallStore(0); break;
            case 1: WWChecked((a + 2 * S(b)) & 0xFFFF, c); break;
            case 2: WB((a + S(b)) & 0xFFFF, c); break;
            case 3: PutProp(a, b, c); break;
            case 4: // sread / aread
                if (Version <= 3) UpdateStatus();
                pendingRead = new PendingRead(false, a, b, Version >= 5 ? RB(pc++) : -1);
                break;
            case 5: PrintZscii(a); break;
            case 6: Print(S(a).ToString()); break;
            case 7: Store(Random(S(a))); break;
            case 8: WriteVar(0, a); break;
            case 9:
                if (Version == 6)
                {
                    // pull (stack) -> result: from a user stack, or the game stack.
                    int v = opCount > 0 && a != 0 ? UserStackPop(a) : ReadVar(0);
                    Store(v);
                }
                else WriteVarRef(a, ReadVar(0));
                break;
            case 10: SplitWindow(a); break;
            case 11:
                if (Version == 6) { SetWindow6(S(a)); break; }
                window = a == 1 ? 1 : 0; if (window == 1) { cursorRow = 0; cursorCol = 0; } break;
            case 12: CallStore(0); break;
            case 13: EraseWindow(S(a)); break;
            case 14: break;   // erase_line
            case 15:
                if (Version == 6) { SetCursor6(S(a), S(b), opCount > 2 ? S(c) : -3); break; }
                if (window == 1) { cursorRow = Math.Max(0, S(a) - 1); cursorCol = Math.Max(0, S(b) - 1); }
                break;
            case 16:
                if (Version == 6) { WW(a, wprop[window6, 4]); WW(a + 2, wprop[window6, 5]); break; }
                WW(a, cursorRow + 1); WW(a + 2, cursorCol + 1);
                break;
            case 17: break;   // set_text_style
            case 18: break;   // buffer_mode
            case 19: OutputStream(S(a), b); break;
            case 20: break;   // input_stream
            case 21: if (opCount == 0 || a is 1 or 2) BeepRequested = true; break;
            case 22: pendingRead = new PendingRead(true, 0, 0, RB(pc++)); break;
            case 23: ScanTable(a, b, c, opCount > 3 ? ops[3] : 0x82); break;
            case 24: Store(~a); break;
            case 25: CallDiscard(0); break;
            case 26: CallDiscard(0); break;
            case 27: Tokenise(a, b, opCount > 2 ? c : 0, opCount > 3 && ops[3] != 0); break;
            case 28: EncodeText(a, b, c, ops[3]); break;
            case 29: CopyTable(a, b, S(c)); break;
            case 30: PrintTable(a, b, opCount > 2 ? c : 1, opCount > 3 ? ops[3] : 0); break;
            case 31: Branch(a <= Top.ArgCount); break;
            default: throw new ZException($"Unknown VAR opcode {n}.");
        }
    }

    private void Ext(int n)
    {
        int a = opCount > 0 ? ops[0] : 0, b = opCount > 1 ? ops[1] : 0;
        switch (n)
        {
            case 0: SaveRequested = true; Store(1); break;
            case 1: RestoreRequested = true; Store(0); break;
            case 2: Store(S(b) >= 0 ? (a << S(b)) & 0xFFFF : a >> -S(b)); break;
            case 3: Store(S(b) >= 0 ? S(a) << S(b) : S(a) >> -S(b)); break;
            case 4: Store(a is 1 or 4 or 0 ? 1 : 0); break;   // set_font
            case 5: PicturesDrawn.Add(a); break;                 // draw_picture
            case 6: PictureData(a, b); break;
            case 7: break;                                       // erase_picture
            case 8: if (Version == 6) SetWindowProps(opCount > 2 ? S(ops[2]) : -3, (6, a), (7, b)); break;   // set_margins
            case 0x10: SetWindowProps(S(a), (0, b), (1, opCount > 2 ? ops[2] : 0)); break;   // move_window
            case 0x11: SetWindowProps(S(a), (2, b), (3, opCount > 2 ? ops[2] : 0)); break;   // window_size
            case 0x12: WindowStyle(S(a), b, opCount > 2 ? ops[2] : 0); break;
            case 0x13: Store(wprop[WindowIndex(S(a)), Math.Clamp(b, 0, WindowProps - 1)] & 0xFFFF); break;   // get_wind_prop
            case 0x14: break;                                    // scroll_window
            case 0x15: UserStackFree(b == 0 ? -1 : b, a); break; // pop_stack
            case 0x16: for (int i = 0; i < 4; i++) WW(a + 2 * i, 0); break;   // read_mouse
            case 0x17: break;                                    // mouse_window
            case 0x18: Branch(UserStackPush(b, a)); break;       // push_stack
            case 0x19: SetWindowProps(S(a), (Math.Clamp(b, 0, WindowProps - 1), opCount > 2 ? ops[2] : 0)); break;   // put_wind_prop
            case 0x1A: PrintForm(a); break;
            case 0x1B: Branch(false); break;                     // make_menu (no menu bar)
            case 0x1C: break;                                    // picture_table
            case 0x1D: Store(0); break;                          // buffer_screen
            case 9: // save_undo
            {
                int storeVar = RB(pc++);
                undo.Add(TakeSnapshot(storeVar));
                if (undo.Count > UndoLevels) undo.RemoveAt(0);
                WriteVar(storeVar, 1);
                break;
            }
            case 10: // restore_undo
            {
                int storeVar = RB(pc++);
                if (undo.Count == 0) { WriteVar(storeVar, 0); break; }
                var u = undo[^1];
                undo.RemoveAt(undo.Count - 1);
                ApplySnapshot(u);
                WriteVar(u.StoreVar, 2);
                break;
            }
            case 11: Print(char.ConvertFromUtf32(Math.Clamp(a, 0, 0x10FFFF))); break;
            case 12: Store(a < 0xD800 || a is >= 0xE000 and <= 0xFFFF ? 3 : 0); break;
            case 13: break;   // set_true_colour
            default:
                // Other extended opcodes are for V6 or unused: skip, storing 0 where a result may be expected.
                break;
        }
    }

    private void Throw(int value, int frameCount)
    {
        while (frames.Count > frameCount && frames.Count > 1) FrameDrop();
        Return(value);
    }

    private void FrameDrop()
    {
        var f = frames[^1];
        frames.RemoveAt(frames.Count - 1);
        stack.RemoveRange(f.StackBase, stack.Count - f.StackBase);
    }

    private int Random(int range)
    {
        if (range > 0) return random.Next(range) + 1;
        random = range == 0 ? (fixedSeed.HasValue ? new Random(fixedSeed.Value) : new Random()) : new Random(-range);
        return 0;
    }

    private sealed class ZException(string message) : Exception(message);
}
