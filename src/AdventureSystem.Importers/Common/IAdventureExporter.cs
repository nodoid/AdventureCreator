using AdventureSystem.Core.Model;

namespace AdventureSystem.Importers.Common;

/// <summary>Writes a game in another system's format (the reverse of an <see cref="IAdventureImporter"/>).</summary>
public interface IAdventureExporter
{
    /// <summary>The system id, matching the importer's (<see cref="SourceOrigin.System"/>).</summary>
    string Id { get; }
    /// <summary>Display name, e.g. "Twine 2 (Twee source)".</summary>
    string Name { get; }
    /// <summary>File extension, without the dot, for this game.</summary>
    string Extension(Adventure adventure);
    /// <summary>Why this game can't be exported in this format, or null if it can.</summary>
    string? CannotExport(Adventure adventure);
    ExportResult Export(Adventure adventure);
}

public sealed class ExportResult
{
    public ExportResult(byte[] data) => Data = data;
    public byte[] Data { get; }
    /// <summary>Things that couldn't be represented in the target format.</summary>
    public List<string> Warnings { get; } = new();
    /// <summary>A summary for the report, e.g. "12 passages, 3 pictures".</summary>
    public string Summary { get; set; } = "";
}

public static class ExporterRegistry
{
    private static readonly List<IAdventureExporter> exporters = new()
    {
        new ZCode.ZCodeExporter(),
        new Twine.TwineExporter(),
        new ScottAdams.ScottAdamsExporter(),
        new Quest.QuestExporter(),
        new Paws.PawsExporter(),
        new Quill.QuillExporter(),
        new Gac.GacExporter(),
    };

    public static IReadOnlyList<IAdventureExporter> All => exporters;

    /// <summary>The exporters for a game, the one for its original format first.</summary>
    public static IEnumerable<IAdventureExporter> For(Adventure adventure) =>
        exporters.OrderBy(e => e.Id == adventure.Origin?.System ? 0 : 1);

    public static IAdventureExporter? Original(Adventure adventure) =>
        adventure.Origin == null ? null : exporters.FirstOrDefault(e => e.Id == adventure.Origin.System);
}

/// <summary>Collects warnings, counting repeats.</summary>
internal sealed class ExportWarnings
{
    private readonly Dictionary<string, int> counts = new();
    public void Add(string what) => counts[what] = counts.TryGetValue(what, out var n) ? n + 1 : 1;
    public void CopyTo(List<string> list)
    {
        foreach (var (what, n) in counts.OrderByDescending(k => k.Value))
            list.Add(n == 1 ? what : $"{what} ({n} times)");
    }
}
