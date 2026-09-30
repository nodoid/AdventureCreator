using System.Text.Json;

namespace AdventureSystem.Core.Engine;

/// <summary>A saved position with the details players see in the load list.</summary>
public sealed class SaveGame
{
    public int Format { get; set; } = 1;
    public string Name { get; set; } = "";
    public string GameTitle { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public string RoomName { get; set; } = "";
    public int Score { get; set; }
    public int MaxScore { get; set; }
    public int Turns { get; set; }
    /// <summary>The serialised <see cref="GameState"/>.</summary>
    public string State { get; set; } = "";

    public bool IsAutosave => string.Equals(Name, GameEngine.AutosaveSlot, StringComparison.OrdinalIgnoreCase);

    public string Summary => $"{(IsAutosave ? "Autosave" : Name)} — {RoomName}, score {Score}/{MaxScore}, turn {Turns} ({SavedAt:g})";

    private static readonly JsonSerializerOptions options = new() { WriteIndented = false };
    public string Serialize() => JsonSerializer.Serialize(this, options);

    /// <summary>Reads a save file. Older saves that contain only the game state are accepted too.</summary>
    public static SaveGame? Parse(string data, string slot)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("State", out var st) && st.ValueKind == JsonValueKind.String)
                return JsonSerializer.Deserialize<SaveGame>(data, options);
            return new SaveGame { Name = slot, State = data };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Where save games are kept. Hosts replace the default in-memory store with file storage.</summary>
public interface ISaveStorage
{
    void Save(string slot, string data);
    string? Load(string slot);
    /// <summary>Names of all saved slots.</summary>
    IReadOnlyList<string> Slots();
    void Delete(string slot);
}

public sealed class MemorySaveStorage : ISaveStorage
{
    private readonly Dictionary<string, string> slots = new(StringComparer.OrdinalIgnoreCase);
    public void Save(string slot, string data) => slots[slot] = data;
    public string? Load(string slot) => slots.TryGetValue(slot, out var d) ? d : null;
    public IReadOnlyList<string> Slots() => slots.Keys.ToList();
    public void Delete(string slot) => slots.Remove(slot);
}

/// <summary>One file per slot (<c>&lt;slot&gt;.sav</c>) in a folder, usually per game.</summary>
public sealed class FileSaveStorage : ISaveStorage
{
    private readonly string directory;

    public FileSaveStorage(string directory)
    {
        this.directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Folder => directory;

    private static string FileName(string slot)
    {
        var safe = new string(slot.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_').ToArray()).Trim();
        return (safe.Length == 0 ? "save" : safe) + ".sav";
    }

    private string PathFor(string slot) => Path.Combine(directory, FileName(slot));

    public void Save(string slot, string data)
    {
        var tmp = PathFor(slot) + ".tmp";
        File.WriteAllText(tmp, data);
        File.Move(tmp, PathFor(slot), overwrite: true);
    }

    public string? Load(string slot) => File.Exists(PathFor(slot)) ? File.ReadAllText(PathFor(slot)) : null;

    public IReadOnlyList<string> Slots() =>
        System.IO.Directory.Exists(directory)
            ? System.IO.Directory.GetFiles(directory, "*.sav").Select(Path.GetFileNameWithoutExtension).Where(n => n != null).Select(n => n!).ToList()
            : new List<string>();

    public void Delete(string slot)
    {
        if (File.Exists(PathFor(slot))) File.Delete(PathFor(slot));
    }
}
