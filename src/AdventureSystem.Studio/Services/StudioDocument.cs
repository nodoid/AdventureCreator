using AdventureSystem.Core.Model;
using AdventureSystem.Core.Packaging;

namespace AdventureSystem.Studio.Services;

/// <summary>The adventure being edited, its file location and unsaved-changes state.</summary>
public sealed class StudioDocument
{
    public StudioDocument(Adventure adventure, string? path = null)
    {
        Adventure = adventure;
        Path = path;
    }

    public Adventure Adventure { get; }
    public string? Path { get; set; }
    public bool Dirty { get; private set; }

    public string DisplayName => Path != null ? System.IO.Path.GetFileName(Path) : (string.IsNullOrWhiteSpace(Adventure.Title) ? "Untitled" : Adventure.Title);

    /// <summary>Raised after any edit. The argument is the edited object (or null).</summary>
    public event Action<object?>? Changed;
    public event Action? DirtyChanged;

    public void MarkChanged(object? what = null)
    {
        if (!Dirty)
        {
            Dirty = true;
            DirtyChanged?.Invoke();
        }
        Changed?.Invoke(what);
    }

    /// <summary>
    /// Writes the saved document where it lives (the iPad, where documents are edited in place and saved
    /// automatically). Null where documents are saved to a path.
    /// </summary>
    public Action<byte[]>? Writer { get; set; }

    /// <summary>Saves with <see cref="Writer"/> if there are unsaved changes.</summary>
    public void SaveInPlace()
    {
        if (Writer == null || !Dirty) return;
        Writer(AdventurePackage.SaveToBytes(Adventure));
        Dirty = false;
        DirtyChanged?.Invoke();
    }

    public void Save(string path)
    {
        AdventurePackage.Save(Adventure, path);
        Path = path;
        Dirty = false;
        DirtyChanged?.Invoke();
        RecentFiles.Add(path);
    }

    public static StudioDocument Open(string path)
    {
        var doc = new StudioDocument(AdventurePackage.Load(path), path);
        RecentFiles.Add(path);
        return doc;
    }

    public static StudioDocument CreateNew()
    {
        var a = new Adventure { Title = "Untitled Adventure", StartRoomId = "room1", Introduction = "Welcome to your new adventure." };
        a.Rooms.Add(new Room { Id = "room1", Name = "First Room", Description = "You are standing in an empty room. Nothing has been created here yet." });
        return new StudioDocument(a);
    }
}

public static class RecentFiles
{
    private const string Key = "recent-files";

    public static IReadOnlyList<string> All =>
        Preferences.Default.Get(Key, "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToList();

    public static void Add(string path)
    {
        var list = All.Where(p => !string.Equals(p, path, StringComparison.Ordinal)).Prepend(path).Take(10);
        Preferences.Default.Set(Key, string.Join('\n', list));
    }
}
