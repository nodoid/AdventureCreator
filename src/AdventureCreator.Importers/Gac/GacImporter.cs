using AdventureCreator.Core.Model;
using AdventureCreator.Core.Snapshots;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Gac;

/// <summary>
/// Imports games written with Incentive Software's <b>Graphic Adventure Creator</b> (GAC, 1985/86).
/// <para>Supported containers:</para>
/// <list type="bullet">
/// <item>ZX Spectrum snapshots (.sna, .z80) of a compiled GAC game (the runtime keeps its punctuation table
/// "\0 .,-!?:" at $A1E5 and the ten database pointers at $A51F);</item>
/// <item>ZX Spectrum tapes (.tap, .tzx with standard/turbo/pure-data blocks): either an unprotected compiled game,
/// or a GAC <i>data file</i> saved from the editor (a CODE block loaded at $A51F = 42271, which starts with the
/// pointer table). When a tape holds several data files (the GAC tape itself has "QS", "ADVINMAN" and "RANSOM"),
/// the largest one whose word dictionary decodes is imported and the others are reported;</item>
/// <item>Amstrad CPC snapshots (.sna, "MV - SNA" header) using the CPC pointer table at $4000 (layout taken from
/// published reverse-engineering; not verified against a real CPC game).</item>
/// </list>
/// <para>Mapping to the <see cref="Adventure"/> model (also written to <see cref="Adventure.Notes"/>):</para>
/// <list type="bullet">
/// <item>GAC turn order: high priority table every turn (before input) → connection table (a verb that is an exit
/// of the room moves the player and ends the turn) → local table of the room → low priority table; a WAIT/OKAY/EXIT
/// ends the turn. High priority conditions become <see cref="TriggerEvent.EveryTurn"/> triggers (priority 30000
/// downwards) plus <see cref="TriggerEvent.GameStart"/> copies of those that don't test the command (GAC runs the
/// table once before the first input); local conditions become <see cref="TriggerEvent.BeforeCommand"/> triggers
/// restricted to the room (priority 20000 downwards) and low priority conditions BeforeCommand triggers (priority
/// 10000 downwards). All command triggers have StopsCommand = false (LegacyTableSemantics); WAIT → Done.</item>
/// <item>The postfix condition bytecode is evaluated symbolically, converted to disjunctive normal form and emitted
/// as one trigger per disjunct (OR/XOR split; NOT → Negate). VERB/NOUN/ADVE become the Verb/Noun1(/Noun2)/Adverb
/// pattern. Command triggers that test a verb used as an exit get "not in room X" conditions for every room where
/// that verb is an exit, because GAC moves the player before looking at the tables.</item>
/// <item>Markers → variables "m{n}" (0/1, marker 1 "light" starts at 1), counters → "c{n}"; counter 0 is the
/// score (→ @score / AwardScore), counters 126/127 and TURN are the turn count (→ @turns); ROOM → @room.</item>
/// <item>Objects "o{n}" (GAC links object n to noun n); room 0 = nowhere, room 255 = carried.</item>
/// </list>
/// </summary>
public sealed class GacImporter : IAdventureImporter
{
    public string Name => "Graphic Adventure Creator";
    public IReadOnlyList<string> Extensions => new[] { "sna", "z80", "tap", "tzx" };

    public bool CanImport(byte[] data, string fileName)
    {
        try { return Locate(data, fileName) != null; }
        catch { return false; }
    }

    public ImportResult Import(byte[] data, string fileName)
    {
        var found = Locate(data, fileName)
            ?? throw new InvalidDataException("No Graphic Adventure Creator database was found in this file.");
        var db = new GacReader(found.Memory, found.Layout, found.Punctuation).Read();
        var title = Path.GetFileNameWithoutExtension(fileName);
        var converter = new GacConverter(db, string.IsNullOrWhiteSpace(title) ? "GAC adventure" : title);
        var adventure = converter.Convert();
        var result = new ImportResult(adventure) { DetectedFormat = found.Format };
        result.Warnings.AddRange(found.Warnings);
        result.Warnings.AddRange(db.Warnings);
        result.Warnings.AddRange(converter.Warnings);
        foreach (var w in found.Warnings.Concat(db.Warnings)) adventure.Notes.Add(w);
        return result;
    }

    internal sealed record Located(byte[] Memory, GacLayout Layout, string Format, byte[]? Punctuation, List<string> Warnings);

    internal static Located? Locate(byte[] data, string fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        if (data.Length >= 0x100 && StartsWith(data, "MV - SNA"))
            return LocateCpc(data);
        if (ext is "tap" or "tzx" || GacTape.IsTzx(data))
            return LocateTape(data);
        if (ext is "sna" or "z80" || (ext == "" && data.Length is 49179 or 131103 or 147487))
        {
            SpectrumSnapshot snap;
            try { snap = SpectrumSnapshot.Load(data, ext); }
            catch { return null; }
            var layout = FindSpectrumLayout(snap.Memory);
            if (layout == null) return null;
            return new Located(snap.Memory, layout, $"GAC (ZX Spectrum, {snap.Format} snapshot)", null, new List<string>());
        }
        return null;
    }

    private static bool StartsWith(byte[] data, string s)
    {
        for (int i = 0; i < s.Length; i++) if (data[i] != s[i]) return false;
        return true;
    }

    /// <summary>Checks the standard runtime address first, then searches for a relocated punctuation table.</summary>
    internal static GacLayout? FindSpectrumLayout(byte[] mem)
    {
        var std = GacLayout.Spectrum();
        if (std.HasPunctuationMagic(mem) && std.PointersLookRight(mem, std.Verbs))
            return std;
        var magic = GacLayout.PunctuationMagic;
        for (int a = 0x5C00; a < 0x10000 - 0x400; a++)
        {
            if (mem[a] != 0 || mem[a + 1] != ' ' || mem[a + 2] != '.') continue;
            bool ok = true;
            for (int k = 3; k < magic.Length && ok; k++) ok = mem[a + k] == magic[k];
            if (!ok) continue;
            var l = GacLayout.Spectrum(a - 0xA1E5);
            if (l.PointersLookRight(mem, l.Verbs)) return l;
        }
        return null;
    }

    private static Located? LocateCpc(byte[] data)
    {
        var mem = new byte[65536];
        int n = Math.Min(65536, data.Length - 0x100);
        Array.Copy(data, 0x100, mem, 0, n);
        var l = GacLayout.AmstradCpc;
        if (!l.HasPunctuationMagic(mem) || !l.PointersLookRight(mem, l.Punctuation)) return null;
        return new Located(mem, l, "GAC (Amstrad CPC snapshot)", null, new List<string>
        {
            "Amstrad CPC GAC support follows the published table layout ($4000 pointers) and has not been verified against a real CPC game.",
        });
    }

    private static Located? LocateTape(byte[] data)
    {
        var files = GacTape.ReadFiles(data);
        var code = files.Where(f => f.Type == 3 && f.Start >= 0).ToList();
        if (code.Count == 0) return null;

        // 1. A tape of a compiled (unprotected) game: all CODE blocks together hold interpreter + database.
        var mem = new byte[65536];
        foreach (var f in code) Place(mem, f);
        var runtime = FindSpectrumLayout(mem);
        if (runtime != null && code.Count(f => f.Start == 0xA51F) <= 1)
            return new Located(mem, runtime, "GAC (ZX Spectrum tape, compiled game)", null, new List<string>());

        // 2. Data files saved from the GAC editor: CODE at $A51F starting with the pointer table.
        var layout = GacLayout.Spectrum();
        var dataFiles = code.Where(f => f.Start == 0xA51F && f.Data.Length > 0x40 && DataFileLooksRight(f)).ToList();
        if (dataFiles.Count == 0) return null;
        // Prefer files whose word dictionary decodes, then the largest.
        var chosen = dataFiles.OrderByDescending(f => DictionaryDecodes(f)).ThenByDescending(f => f.Data.Length).First();
        var image = new byte[65536];
        foreach (var f in code.Where(f => f.Start != 0xA51F)) Place(image, f);
        Place(image, chosen);
        var warnings = new List<string>();
        if (dataFiles.Count > 1)
            warnings.Add($"The tape holds {dataFiles.Count} GAC data files ({string.Join(", ", dataFiles.Select(f => $"\"{f.Name}\""))}); imported \"{chosen.Name}\" (the largest one whose dictionary decodes).");
        byte[]? punct = layout.HasPunctuationMagic(image) ? null : GacLayout.PunctuationMagic;
        return new Located(image, layout, $"GAC (ZX Spectrum data file \"{chosen.Name}\")", punct, warnings);
    }

    private static void Place(byte[] mem, TapeFile f)
    {
        int n = Math.Min(f.Data.Length, 65536 - f.Start);
        if (n > 0) Array.Copy(f.Data, 0, mem, f.Start, n);
    }

    private static bool DictionaryDecodes(TapeFile f)
    {
        var mem = new byte[65536];
        Place(mem, f);
        try
        {
            var db = new GacReader(mem, GacLayout.Spectrum(), GacLayout.PunctuationMagic).Read();
            return db.Warnings.All(w => !w.Contains("dictionary"));
        }
        catch { return false; }
    }

    private static bool DataFileLooksRight(TapeFile f)
    {
        var mem = new byte[65536];
        Place(mem, f);
        var l = GacLayout.Spectrum();
        if (!l.PointersLookRight(mem, l.Verbs)) return false;
        int tokens = mem[l.Tokens] | (mem[l.Tokens + 1] << 8);
        return tokens < f.Start + f.Data.Length;
    }
}
