using AdventureCreator.Core.Model;

namespace AdventureCreator.Importers.Common;

/// <summary>Converts a game written with a legacy adventure creation system into an <see cref="Adventure"/>.</summary>
public interface IAdventureImporter
{
    /// <summary>Display name, e.g. "PAWS (ZX Spectrum)".</summary>
    string Name { get; }
    /// <summary>File extensions (without dot) this importer accepts.</summary>
    IReadOnlyList<string> Extensions { get; }
    /// <summary>Quick detection: returns true if the data looks like a game this importer handles.</summary>
    bool CanImport(byte[] data, string fileName);
    ImportResult Import(byte[] data, string fileName);
}

public sealed class ImportResult
{
    public ImportResult(Adventure adventure) => Adventure = adventure;
    public Adventure Adventure { get; }
    public List<string> Warnings { get; } = new();
    /// <summary>Name of the detected source system/version, e.g. "Quill (version C) + Illustrator".</summary>
    public string DetectedFormat { get; set; } = "";
}

public static class ImporterRegistry
{
    private static readonly List<IAdventureImporter> importers = new();

    public static IReadOnlyList<IAdventureImporter> All => importers;

    static ImporterRegistry()
    {
        importers.Add(new Paws.PawsImporter());
        importers.Add(new Quill.QuillImporter());
        importers.Add(new Gac.GacImporter());
        importers.Add(new ZCode.ZCodeImporter());
        importers.Add(new ScottAdams.ScottAdamsImporter());
        importers.Add(new Quest.QuestImporter());
        importers.Add(new Twine.TwineImporter());
    }

    /// <summary>File-dialog extensions accepted by any importer.</summary>
    public static IEnumerable<string> AllExtensions => importers.SelectMany(i => i.Extensions).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds the first importer that recognises the data.</summary>
    public static IAdventureImporter? Detect(byte[] data, string fileName) =>
        importers.FirstOrDefault(i => SafeCanImport(i, data, fileName));

    public static ImportResult Import(string path)
    {
        var data = File.ReadAllBytes(path);
        var importer = Detect(data, path)
            ?? throw new InvalidDataException("The file is not a game Adventure Creator can import.");
        return importer.Import(data, path);
    }

    private static bool SafeCanImport(IAdventureImporter i, byte[] data, string fileName)
    {
        try { return i.CanImport(data, fileName); }
        catch { return false; }
    }
}
