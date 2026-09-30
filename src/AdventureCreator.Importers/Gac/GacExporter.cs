using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Gac;

public sealed class GacExporter : IAdventureExporter
{
    public string Id => "gac";
    public string Name => "Graphic Adventure Creator (ZX Spectrum snapshot)";
    public string Extension(Adventure a) => "bin";
    public string? CannotExport(Adventure a) => "Not available yet.";
    public ExportResult Export(Adventure a) => throw new NotSupportedException();
}
