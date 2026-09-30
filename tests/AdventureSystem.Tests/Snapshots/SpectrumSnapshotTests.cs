using AdventureSystem.Core.Snapshots;
using Xunit;

namespace AdventureSystem.Tests.Snapshots;

public class SpectrumSnapshotTests
{
    private static byte[] Ram()
    {
        var rng = new Random(7);
        var ram = new byte[49152];
        rng.NextBytes(ram);
        // Runs, lone EDs and ED runs exercise the Z80 compression rules.
        Array.Fill<byte>(ram, 0, 100, 300);
        Array.Fill<byte>(ram, 0xED, 1000, 2);
        ram[2000] = 0xED; Array.Fill<byte>(ram, 7, 2001, 6);
        return ram;
    }

    [Fact]
    public void Sna48_round_trips_with_changes()
    {
        var file = new byte[49179];
        Ram().CopyTo(file, 27);
        var snap = SpectrumSnapshot.Load(file, "sna");
        snap.Memory[0x8000] ^= 0xFF;
        var again = SpectrumSnapshot.Load(snap.Save(file), "sna");
        Assert.Equal(snap.Memory, again.Memory);
    }

    [Fact]
    public void Z80v1_round_trips_compressed()
    {
        var file = new byte[30 + 49152];
        file[6] = 0x34; file[7] = 0x12;   // pc: version 1
        Ram().CopyTo(file, 30);
        var snap = SpectrumSnapshot.Load(file, "z80");
        snap.Memory[0xC123] = 0xED;
        var saved = snap.Save(file);
        Assert.True(saved.Length < file.Length);
        Assert.Equal(snap.Memory, SpectrumSnapshot.Load(saved, "z80").Memory);
    }

    [Fact]
    public void Z80v3_128K_round_trips_every_bank()
    {
        var header = new byte[32 + 54];
        header[30] = 54; header[34] = 4;   // v3, 128K
        var file = new List<byte>(header);
        var ram = Ram();
        for (int b = 0; b < 8; b++)
        {
            file.AddRange(new byte[] { 0xFF, 0xFF, (byte)(b + 3) });
            file.AddRange(ram.AsSpan(b * 4096, 16384).ToArray());
        }
        var snap = SpectrumSnapshot.Load(file.ToArray(), "z80");
        snap.Banks128![6][10] ^= 0xFF;
        var again = SpectrumSnapshot.Load(snap.Save(file.ToArray()), "z80");
        for (int b = 0; b < 8; b++) Assert.Equal(snap.Banks128[b], again.Banks128![b]);
    }
}
