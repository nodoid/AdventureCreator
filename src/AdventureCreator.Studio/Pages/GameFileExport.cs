using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;
using AdventureCreator.Importers.Common;
using CommunityToolkit.Maui.Storage;

namespace AdventureCreator.Studio.Pages;

/// <summary>
/// Saves the game as a file: in Adventure Creator's own format, in the format it was imported from, or in
/// another system's format, with a report of anything that format couldn't hold.
/// </summary>
internal static class GameFileExport
{
    public const string CreatorFormat = "Adventure Creator (.adventure)";

    /// <summary>The choices for a game: its original format first, then the Creator format, then the others that can take it.</summary>
    public static List<(string Label, IAdventureExporter? Exporter)> Choices(Adventure a)
    {
        var list = new List<(string, IAdventureExporter?)>();
        var original = ExporterRegistry.Original(a);
        if (original != null && original.CannotExport(a) == null)
            list.Add(($"{original.Name} (.{original.Extension(a)}) – original format", original));
        list.Add((CreatorFormat, null));
        foreach (var e in ExporterRegistry.All.Where(e => e != original && e.CannotExport(a) == null))
            list.Add(($"{e.Name} (.{e.Extension(a)})", e));
        return list;
    }

    /// <summary>Asks which format, then exports.</summary>
    public static async Task ChooseAndExportAsync(Page page, Adventure a)
    {
        var choices = Choices(a);
        var picked = await page.DisplayActionSheetAsync("Export the game as", "Cancel", null, choices.Select(c => c.Label).ToArray());
        if (picked == null || picked == "Cancel") return;
        var choice = choices.First(c => c.Label == picked);
        await ExportAsync(page, a, choice.Exporter);
    }

    /// <summary>Exports with <paramref name="exporter"/> (null = the Creator format) and shows what happened.</summary>
    public static async Task ExportAsync(Page page, Adventure a, IAdventureExporter? exporter)
    {
        try
        {
            byte[] data;
            string name, report;
            if (exporter == null)
            {
                data = AdventurePackage.SaveToBytes(a);
                name = StandaloneExporter.SafeFileName(a.Title) + AdventurePackage.Extension;
                report = "";
            }
            else
            {
                var result = await Task.Run(() => exporter.Export(a));
                data = result.Data;
                var baseName = exporter == ExporterRegistry.Original(a) && a.Origin?.FileName is { Length: > 0 } f
                    ? Path.GetFileNameWithoutExtension(f)
                    : StandaloneExporter.SafeFileName(a.Title);
                name = $"{baseName}.{exporter.Extension(a)}";
                report = $"Exported as {exporter.Name}: {result.Summary}.\n\n" +
                         (result.Warnings.Count > 0 ? "Not everything fits this format:\n• " + string.Join("\n• ", result.Warnings) : "Everything was exported.");
            }
            var saved = await FileSaver.Default.SaveAsync(name, new MemoryStream(data), CancellationToken.None);
            if (!saved.IsSuccessful)
            {
                if (saved.Exception is not null and not TaskCanceledException and not OperationCanceledException)
                    await page.DisplayAlertAsync("Export failed", saved.Exception.Message, "OK");
                return;
            }
            if (report.Length > 0) await page.Navigation.PushModalAsync(new ReportPage("Export report", $"Saved {saved.FilePath}\n\n{report}"));
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Export failed", ex.Message, "OK");
        }
    }
}
