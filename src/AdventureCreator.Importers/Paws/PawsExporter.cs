using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Paws;

public sealed class PawsExporter : IAdventureExporter
{
    public string Id => "paws";
    public string Name => "PAWS (ZX Spectrum snapshot)";
    public string Extension(Adventure a) => "bin";
    public string? CannotExport(Adventure a) => "Not available yet.";
    public ExportResult Export(Adventure a) => throw new NotSupportedException();
}
