using AdventureCreator.Core.Model;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Quill;

public sealed class QuillExporter : IAdventureExporter
{
    public string Id => "quill";
    public string Name => "The Quill (ZX Spectrum snapshot)";
    public string Extension(Adventure a) => "bin";
    public string? CannotExport(Adventure a) => "Not available yet.";
    public ExportResult Export(Adventure a) => throw new NotSupportedException();
}
