using AdventureSystem.Core.Model;
using AdventureSystem.Core.Snapshots;
using AdventureSystem.Importers.Common;

namespace AdventureSystem.Importers.Gac;

/// <summary>
/// Writes a game imported from Graphic Adventure Creator back into the file it came from (.sna / .z80 snapshot,
/// Amstrad snapshot, or a .tap / .tzx tape of a compiled game or GAC data file). The original database is the
/// template: unchanged parts keep their bytes, edits are compiled (see <see cref="GacCompiler"/>) and the database
/// is laid out again in the memory it occupied.
/// </summary>
public sealed class GacExporter : IAdventureExporter
{
    public string Id => "gac";
    public string Name => "Graphic Adventure Creator";

    public string Extension(Adventure a)
    {
        var ext = Path.GetExtension(a.Origin?.FileName ?? "").TrimStart('.').ToLowerInvariant();
        return ext.Length > 0 ? ext : "sna";
    }

    public string? CannotExport(Adventure a)
    {
        if (a.IsStory) return "Z-code stories can't be converted.";
        if (Original(a) == null) return "Needs the original GAC file; this game wasn't imported from one.";
        return null;
    }

    private static byte[]? Original(Adventure a) =>
        a.Origin is { System: "gac", OriginalAsset: { } asset } && a.Assets.TryGetValue(asset, out var data) ? data : null;

    public ExportResult Export(Adventure a) => Export(a, forceRebuild: false);

    /// <param name="forceRebuild">Lay the database out again even when nothing changed (tests the writer).</param>
    internal ExportResult Export(Adventure a, bool forceRebuild)
    {
        if (CannotExport(a) is { } why) throw new InvalidOperationException(why);
        var original = Original(a)!;
        var fileName = a.Origin!.FileName;
        var found = GacImporter.Locate(original, fileName)
                    ?? throw new InvalidDataException("The original file no longer holds a GAC database.");
        var baseline = new GacImporter().Import(original, fileName).Adventure;
        var memory = (byte[])found.Memory.Clone();
        var rawDb = new GacRawDatabase(memory, found.Layout, found.Punctuation,
            found.DataFile is { } file ? file.Start + file.Data.Length : 0x10000);
        var decoded = new GacReader(memory, found.Layout, found.Punctuation).Read();
        var warnings = new ExportWarnings();
        var compiler = new GacCompiler(a, baseline, rawDb, decoded, warnings);
        compiler.Run();

        if (rawDb.IncompleteDictionary)
            warnings.Add("The original file's word dictionary is incomplete (some of its texts were already unreadable); they stay unreadable");
        byte[] data;
        if (!compiler.Changed && !forceRebuild) data = original;
        else
        {
            var database = rawDb.Serialize();
            bool dataFile = found.DataFile != null;
            int limit = dataFile ? 0xFF58 : rawDb.FreeLimit();
            // A data file keeps the padding the editor saved after the dictionary.
            int tail = found.DataFile is { } df ? Math.Max(0, df.Start + df.Data.Length - rawDb.End) : 2;
            int end = rawDb.Start + database.Length;
            if (end + tail > limit)
                throw new InvalidOperationException($"The game is {end + tail - limit} bytes too big for the memory the GAC database can use.");
            var padding = dataFile ? memory.AsSpan(rawDb.End, tail).ToArray() : new byte[tail];
            database.CopyTo(memory, rawDb.Start);
            // Clear what the old database used beyond the new one, so stale bytes don't look like tables (a data
            // file's padding is kept as it was).
            Array.Clear(memory, end, Math.Max(0, rawDb.End - end));
            padding.CopyTo(memory, end);
            data = Write(original, fileName, found, memory, rawDb.Start, end + (dataFile ? tail : 0));
        }

        var result = new ExportResult(data)
        {
            Summary = $"{rawDb.Rooms.Count} rooms, {rawDb.Objects.Count} objects, {rawDb.Messages.Count} messages" +
                      (compiler.Changed ? "" : " (unchanged: the original file)"),
        };
        warnings.CopyTo(result.Warnings);
        return result;
    }

    /// <summary>The original file with the new memory contents, in the same format.</summary>
    private static byte[] Write(byte[] original, string fileName, GacImporter.Located found, byte[] memory, int start, int end)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (found.Layout.Machine == GacMachine.AmstradCpc)
        {
            var cpc = (byte[])original.Clone();
            Array.Copy(memory, 0, cpc, 0x100, Math.Min(65536, cpc.Length - 0x100));
            return cpc;
        }
        if (ext is "tap" or "tzx" || GacTape.IsTzx(original)) return WriteTape(original, found, memory, start, end);

        var snap = SpectrumSnapshot.Load(original, ext);
        Array.Copy(memory, 0x4000, snap.Memory, 0x4000, 0xC000);
        if (snap.Banks128 is { } banks)
        {
            int paged = snap.Format == "sna128" ? original[49181] & 7 : original[35] & 7;
            foreach (var (bank, address) in new[] { (5, 0x4000), (2, 0x8000), (paged, 0xC000) })
                if (banks[bank] != null) Array.Copy(memory, address, banks[bank], 0, 16384);
        }
        return snap.Save(original);
    }

    private static byte[] WriteTape(byte[] original, GacImporter.Located found, byte[] memory, int start, int end)
    {
        var blocks = GacTape.ReadBlocks(original);
        var replacements = new Dictionary<int, byte[]>();
        void Replace(TapeFile f, int length)
        {
            if (f.Block > 0 && blocks[f.Block - 1] is { Length: 19 } header && header[0] == 0)
            {
                var (h, d) = GacTape.CodeFile(header, memory.AsSpan(f.Start, length));
                replacements[f.Block - 1] = h;
                replacements[f.Block] = d;
            }
            else replacements[f.Block] = GacTape.Block(0xFF, memory.AsSpan(f.Start, length));
        }

        if (found.DataFile is { } file)
        {
            Replace(file, end - file.Start);
            return GacTape.Rewrite(original, replacements);
        }
        // A compiled game: rewrite every CODE block the database touches; the one it starts in grows if need be.
        foreach (var f in GacTape.ReadFiles(original).Where(f => f.Type == 3 && f.Start >= 0))
        {
            int fEnd = f.Start + f.Data.Length;
            if (fEnd <= start || f.Start >= Math.Max(end, start + 1)) continue;
            int length = f.Start <= start && start < fEnd ? Math.Max(f.Data.Length, end - f.Start) : f.Data.Length;
            Replace(f, Math.Min(length, 65536 - f.Start));
        }
        return GacTape.Rewrite(original, replacements);
    }
}
