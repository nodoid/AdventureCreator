using AdventureCreator.Core.Snapshots;
using AdventureCreator.Importers.Common;

namespace AdventureCreator.Importers.Paws;

/// <summary>
/// Imports games written with Gilsoft's Professional Adventure Writing System (PAWS) for the ZX Spectrum,
/// from 48K or 128K .SNA / .Z80 snapshots, including the vector pictures drawn with the PAWS graphics editor.
/// </summary>
public sealed class PawsImporter : IAdventureImporter
{
    public string Name => "PAWS (ZX Spectrum)";
    public IReadOnlyList<string> Extensions { get; } = new[] { "sna", "z80" };

    public bool CanImport(byte[] data, string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        bool snapshotSize = data.Length is 49179 or 131103 or 147487;
        if (ext is not ("sna" or "z80") && !(ext == "" && snapshotSize)) return false;
        try
        {
            return PawsDatabase.TryOpen(SpectrumSnapshot.Load(data, ext)) != null;
        }
        catch
        {
            return false;
        }
    }

    public ImportResult Import(byte[] data, string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var snap = SpectrumSnapshot.Load(data, ext);
        var db = PawsDatabase.TryOpen(snap)
                 ?? throw new InvalidDataException("No PAWS database was found in the snapshot.");
        return new PawsConversion(db, fileName).Run();
    }
}
