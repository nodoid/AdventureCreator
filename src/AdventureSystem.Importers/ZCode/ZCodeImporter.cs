using AdventureSystem.Core.ZMachine;
using AdventureSystem.Importers.Common;

namespace AdventureSystem.Importers.ZCode;

/// <summary>
/// Z-code stories (Infocom, Inform, ZIL: versions 1–8, raw or in a Blorb). The story isn't converted into rooms and
/// triggers – compiled Z-code can't be turned back into editable logic – but becomes a game that the built-in
/// Z-machine plays everywhere Adventure System games run, with Blorb pictures and cover art.
/// </summary>
public sealed class ZCodeImporter : IAdventureImporter
{
    public string Name => "Z-code story (Infocom / Inform)";
    public string Id => "zcode";
    public IReadOnlyList<string> Extensions => ZStory.Extensions;

    public bool CanImport(byte[] data, string fileName) => ZStory.IsStory(data);

    public ImportResult Import(byte[] data, string fileName)
    {
        var adventure = ZStory.CreateAdventure(data, fileName);
        var machine = ZStory.CreateMachine(adventure);
        var result = new ImportResult(adventure)
        {
            DetectedFormat = $"Z-code version {machine.Version} (release {machine.Release}, serial {machine.Serial.Trim('\0', ' ')})" +
                             (Blorb.IsBlorb(data) ? $" in a Blorb file with {adventure.Pictures.Count} picture(s)" : ""),
        };
        result.Warnings.Add("Z-code is compiled: the story plays exactly as written, on the built-in Z-machine, but its rooms and logic can't be edited here.");
        if (machine.Version == 6 && adventure.Pictures.Count == 0)
            result.Warnings.Add("This is a version 6 (graphical) story without pictures. Use its Blorb file (.zblorb / .blb) to see them.");
        adventure.Notes.Add("Imported Z-code story: " + adventure.Description);
        return result;
    }
}
