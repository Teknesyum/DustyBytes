using DustyBytes.Scan;

namespace DustyBytes.Scan.Tests;

public sealed class DriveCatalogTests
{
    const long Gb = 1_000_000_000;

    static DriveProbe P(string root, DriveType type = DriveType.Fixed, string format = "NTFS", bool ready = true, string? id = null, long total = 500 * Gb, long free = 100 * Gb) =>
        new(root, type, ready, format, "", total, free, id ?? $@"\\?\Volume{{{root[0]}}}\");

    [Fact]
    public void SelectsFixedSupportedDrivesWithSystemFirst()
    {
        var list = DriveCatalog.Select(
        [
            P(@"E:\", format: "exFAT"),
            P(@"D:\", format: "ReFS"),
            P(@"C:\"),
            P(@"F:\", format: "FAT32"),
            P(@"G:\", DriveType.Network),
            P(@"H:\", DriveType.CDRom, "CDFS"),
            P(@"I:\", ready: false),
            P(@"J:\", total: 0),
        ], false, @"C:\");

        Assert.Equal([@"C:\", @"D:\", @"E:\"], list.Select(d => d.Root));
        Assert.Equal(DriveKind.System, list[0].Kind);
        Assert.All(list.Skip(1), d => Assert.Equal(DriveKind.Fixed, d.Kind));
    }

    [Fact]
    public void RemovableOnlyWhenAsked()
    {
        DriveProbe[] probes = [P(@"C:\"), P(@"K:\", DriveType.Removable, "exFAT")];

        Assert.Equal([@"C:\"], DriveCatalog.Select(probes, false, @"C:\").Select(d => d.Root));
        var with = DriveCatalog.Select(probes, true, @"C:\");
        Assert.Equal([@"C:\", @"K:\"], with.Select(d => d.Root));
        Assert.Equal(DriveKind.Removable, with[1].Kind);
    }

    [Fact]
    public void SameVolumeUnderTwoLettersIsScannedOnce()
    {
        var list = DriveCatalog.Select([P(@"C:\", id: "v1"), P(@"S:\", id: "v1"), P(@"T:\", id: null) with { VolumeId = null }], false, @"c:");

        Assert.Equal([@"C:\"], list.Select(d => d.Root));
    }

    [Fact]
    public void UsedShareFollowsFreeSpace()
    {
        var drive = DriveCatalog.Select([P(@"C:\", total: 400 * Gb, free: 100 * Gb)], false, @"C:\").Single();

        Assert.Equal(300 * Gb, drive.UsedBytes);
        Assert.Equal(0.75, drive.UsedShare, 3);
        Assert.Equal("C:", drive.Letter);
    }

    [Fact]
    public void SystemDriveIsListedOnThisMachine()
    {
        var list = DriveCatalog.List(false);

        Assert.NotEmpty(list);
        Assert.Equal(DriveCatalog.SystemRoot, list[0].Root);
        Assert.Equal(DriveKind.System, list[0].Kind);
    }
}
