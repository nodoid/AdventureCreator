using System.Text;

namespace AdventureSystem.Core.ZMachine;

public sealed partial class ZMachine
{
    // ================================================================= object table

    private int ObjectTable => RW(0x0A);
    private int EntrySize => Version <= 3 ? 9 : 14;
    private int DefaultsSize => Version <= 3 ? 31 : 63;

    private int ObjAddr(int obj)
    {
        if (obj <= 0) throw new ZException($"Bad object number {obj}.");
        return ObjectTable + DefaultsSize * 2 + (obj - 1) * EntrySize;
    }

    private int Parent(int obj) => obj == 0 ? 0 : Version <= 3 ? RB(ObjAddr(obj) + 4) : RW(ObjAddr(obj) + 6);
    private int Sibling(int obj) => obj == 0 ? 0 : Version <= 3 ? RB(ObjAddr(obj) + 5) : RW(ObjAddr(obj) + 8);
    private int Child(int obj) => obj == 0 ? 0 : Version <= 3 ? RB(ObjAddr(obj) + 6) : RW(ObjAddr(obj) + 10);

    private void SetParent(int obj, int v) { if (Version <= 3) mem[ObjAddr(obj) + 4] = (byte)v; else WW(ObjAddr(obj) + 6, v); }
    private void SetSibling(int obj, int v) { if (Version <= 3) mem[ObjAddr(obj) + 5] = (byte)v; else WW(ObjAddr(obj) + 8, v); }
    private void SetChild(int obj, int v) { if (Version <= 3) mem[ObjAddr(obj) + 6] = (byte)v; else WW(ObjAddr(obj) + 10, v); }

    private int PropTable(int obj) => RW(ObjAddr(obj) + (Version <= 3 ? 7 : 12));

    private bool TestAttr(int obj, int attr)
    {
        if (obj == 0) return false;
        return (RB(ObjAddr(obj) + attr / 8) & (0x80 >> (attr % 8))) != 0;
    }

    private void SetAttr(int obj, int attr, bool on)
    {
        if (obj == 0) return;
        int a = ObjAddr(obj) + attr / 8;
        int bit = 0x80 >> (attr % 8);
        mem[a] = (byte)(on ? mem[a] | bit : mem[a] & ~bit);
    }

    private void RemoveObj(int obj)
    {
        if (obj == 0) return;
        int parent = Parent(obj);
        if (parent == 0) return;
        int first = Child(parent);
        if (first == obj) SetChild(parent, Sibling(obj));
        else
        {
            for (int o = first; o != 0; o = Sibling(o))
                if (Sibling(o) == obj) { SetSibling(o, Sibling(obj)); break; }
        }
        SetParent(obj, 0);
        SetSibling(obj, 0);
    }

    private void InsertObj(int obj, int dest)
    {
        if (obj == 0 || dest == 0) return;
        RemoveObj(obj);
        SetParent(obj, dest);
        SetSibling(obj, Child(dest));
        SetChild(dest, obj);
    }

    /// <summary>An object's short name.</summary>
    public string ObjectName(int obj)
    {
        if (obj == 0) return "";
        int p = PropTable(obj);
        return RB(p) == 0 ? "" : DecodeString(p + 1, out _);
    }

    // ================================================================= properties

    private int FirstProp(int obj)
    {
        int p = PropTable(obj);
        return p + 1 + RB(p) * 2;
    }

    /// <summary>Property at <paramref name="addr"/>: its number, data address and length (number 0 = end of list).</summary>
    private (int Number, int Data, int Length) PropAt(int addr)
    {
        int b = RB(addr);
        if (Version <= 3) return (b & 31, addr + 1, (b >> 5) + 1);
        if ((b & 0x80) != 0)
        {
            int len = RB(addr + 1) & 63;
            return (b & 63, addr + 2, len == 0 ? 64 : len);
        }
        return (b & 63, addr + 1, (b & 0x40) != 0 ? 2 : 1);
    }

    private (int Data, int Length)? FindProp(int obj, int prop)
    {
        if (obj == 0) return null;
        for (int a = FirstProp(obj); ;)
        {
            var (num, data, len) = PropAt(a);
            if (num == 0 || RB(a) == 0) return null;
            if (num == prop) return (data, len);
            if (num < prop) return null;   // properties are stored in descending order
            a = data + len;
        }
    }

    private int GetProp(int obj, int prop)
    {
        if (FindProp(obj, prop) is { } p) return p.Length == 1 ? RB(p.Data) : RW(p.Data);
        return RW(ObjectTable + (prop - 1) * 2);
    }

    private int GetPropAddr(int obj, int prop) => FindProp(obj, prop) is { } p ? p.Data : 0;

    private int GetNextProp(int obj, int prop)
    {
        if (obj == 0) return 0;
        if (prop == 0) return PropAt(FirstProp(obj)).Number;
        if (FindProp(obj, prop) is not { } p) throw new ZException($"get_next_prop: object {obj} has no property {prop}.");
        return RB(p.Data + p.Length) == 0 ? 0 : PropAt(p.Data + p.Length).Number;
    }

    private int PropLen(int dataAddr)
    {
        if (dataAddr == 0) return 0;
        int b = RB(dataAddr - 1);
        if (Version <= 3) return (b >> 5) + 1;
        if ((b & 0x80) != 0) { int len = b & 63; return len == 0 ? 64 : len; }
        return (b & 0x40) != 0 ? 2 : 1;
    }

    private void PutProp(int obj, int prop, int value)
    {
        if (FindProp(obj, prop) is not { } p) throw new ZException($"put_prop: object {obj} has no property {prop}.");
        if (p.Length == 1) mem[p.Data] = (byte)value;
        else WW(p.Data, value);
    }

    // ================================================================= text

    private const string A0 = "abcdefghijklmnopqrstuvwxyz";
    private const string A1 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string A2 = " \n0123456789.,!?_#'\"/\\-:()";
    private const string A2V1 = " 0123456789.,!?_#'\"/\\<-:()";

    /// <summary>ZSCII 155–223 by default (the standard "extra characters").</summary>
    private const string DefaultExtra = "äöüÄÖÜß»«ëïÿËÏáéíóúýÁÉÍÓÚÝàèìòùÀÈÌÒÙâêîôûÂÊÎÔÛåÅøØãñõÃÑÕæÆçÇþðÞÐ£œŒ¡¿";

    private char AlphabetChar(int alphabet, int index)
    {
        int table = Version >= 5 ? RW(0x34) : 0;
        if (table != 0) return ZsciiToChar(RB(table + alphabet * 26 + index));
        return alphabet switch { 0 => A0[index], 1 => A1[index], _ => Version == 1 ? A2V1[index] : A2[index] };
    }

    /// <summary>Decodes a Z-encoded string at <paramref name="addr"/>; <paramref name="length"/> is its size in bytes.</summary>
    public string DecodeString(int addr, out int length) => DecodeString(addr, out length, allowAbbreviations: true);

    private string DecodeString(int addr, out int length, bool allowAbbreviations)
    {
        var sb = new StringBuilder();
        int start = addr;
        int alphabet = 0, lockAlphabet = 0;
        int abbrev = 0;
        int zsciiStep = 0, zsciiHigh = 0;
        while (true)
        {
            int w = RW(addr);
            addr += 2;
            for (int shift = 10; shift >= 0; shift -= 5)
            {
                int z = (w >> shift) & 31;
                if (zsciiStep == 1) { zsciiHigh = z; zsciiStep = 2; continue; }
                if (zsciiStep == 2) { sb.Append(ZsciiToChar((zsciiHigh << 5) | z)); zsciiStep = 0; continue; }
                if (abbrev != 0)
                {
                    int entry = RW(RW(0x18) + 2 * (32 * (abbrev - 1) + z));
                    if (allowAbbreviations) sb.Append(DecodeString(entry * 2, out _, allowAbbreviations: false));
                    abbrev = 0;
                    continue;
                }
                switch (z)
                {
                    case 0: sb.Append(' '); alphabet = lockAlphabet; break;
                    case 1 when Version == 1: sb.Append('\n'); alphabet = lockAlphabet; break;
                    case 1:
                    case 2 when Version >= 3:
                    case 3 when Version >= 3:
                        abbrev = z;
                        break;
                    case 2: alphabet = (lockAlphabet + 1) % 3; break;              // V1-2 shift
                    case 3: alphabet = (lockAlphabet + 2) % 3; break;
                    case 4 when Version <= 2: lockAlphabet = alphabet = (lockAlphabet + 1) % 3; break;
                    case 5 when Version <= 2: lockAlphabet = alphabet = (lockAlphabet + 2) % 3; break;
                    case 4: alphabet = 1; break;
                    case 5: alphabet = 2; break;
                    default:
                        if (alphabet == 2 && z == 6) { zsciiStep = 1; alphabet = lockAlphabet; break; }
                        if (alphabet == 2 && z == 7 && Version >= 2) { sb.Append('\n'); alphabet = lockAlphabet; break; }
                        sb.Append(AlphabetChar(alphabet, z - 6));
                        alphabet = lockAlphabet;
                        break;
                }
            }
            if ((w & 0x8000) != 0 || addr >= mem.Length) break;
        }
        length = addr - start;
        return sb.ToString();
    }

    private char ZsciiToChar(int z)
    {
        if (z == 13) return '\n';
        if (z is >= 32 and <= 126) return (char)z;
        if (z is >= 155 and <= 251)
        {
            int ext = Version >= 5 ? UnicodeTable() : 0;
            if (ext != 0)
            {
                int count = RB(ext);
                if (z - 155 < count) return (char)RW(ext + 1 + 2 * (z - 155));
                return '?';
            }
            return z - 155 < DefaultExtra.Length ? DefaultExtra[z - 155] : '?';
        }
        return z == 0 ? '\0' : '?';
    }

    private int CharToZscii(char c)
    {
        if (c == '\n' || c == '\r') return 13;
        if (c is >= ' ' and <= '~') return c;
        int ext = Version >= 5 ? UnicodeTable() : 0;
        if (ext != 0)
        {
            int count = RB(ext);
            for (int i = 0; i < count; i++) if (RW(ext + 1 + 2 * i) == c) return 155 + i;
            return '?';
        }
        int k = DefaultExtra.IndexOf(c);
        return k >= 0 ? 155 + k : '?';
    }

    private int UnicodeTable()
    {
        int extension = RW(0x36);
        if (extension == 0 || RW(extension) < 3) return 0;
        return RW(extension + 6);
    }

    /// <summary>Encodes a word for dictionary lookup (V1–3: 6 Z-characters in 4 bytes; V4+: 9 in 6).</summary>
    private byte[] Encode(string word)
    {
        int zchars = Version <= 3 ? 6 : 9;
        var z = new List<int>();
        foreach (char ch in word.ToLowerInvariant())
        {
            if (z.Count >= zchars) break;
            int i = A0.IndexOf(ch);
            if (Version >= 5 && RW(0x34) != 0)
            {
                // Custom alphabet table.
                int table = RW(0x34);
                int zc = CharToZscii(ch), found = -1;
                for (int k = 0; k < 78 && found < 0; k++) if (RB(table + k) == zc) found = k;
                if (found >= 0)
                {
                    if (found >= 26) z.Add(found >= 52 ? 5 : 4);
                    z.Add(6 + found % 26);
                    continue;
                }
                i = -1;
            }
            if (i >= 0) { z.Add(6 + i); continue; }
            int j = (Version == 1 ? A2V1 : A2).IndexOf(ch);
            if (j >= 2 || (Version == 1 && j >= 1))
            {
                z.Add(Version <= 2 ? 3 : 5);
                z.Add(6 + j);
                continue;
            }
            int zscii = CharToZscii(ch);
            z.Add(Version <= 2 ? 3 : 5);
            z.Add(6);
            z.Add(zscii >> 5);
            z.Add(zscii & 31);
        }
        while (z.Count < zchars) z.Add(5);
        var bytes = new byte[zchars / 3 * 2];
        for (int w = 0; w < zchars / 3; w++)
        {
            int v = (z[w * 3] << 10) | (z[w * 3 + 1] << 5) | z[w * 3 + 2];
            if (w == zchars / 3 - 1) v |= 0x8000;
            bytes[w * 2] = (byte)(v >> 8);
            bytes[w * 2 + 1] = (byte)v;
        }
        return bytes;
    }
}
