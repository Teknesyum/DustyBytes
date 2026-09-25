using DustyBytes.Clean.Uninstall;

namespace DustyBytes.Uninstall.Tests;

public class MatchingTests
{
    [Fact]
    public void OfficeProductCodePacksAndUnpacks()
    {
        Assert.True(MsiGuid.TryParseBraced("{90120000-0030-0000-0000-0000000FF1CE}", out var g));
        Assert.Equal("00002109030000000000000000F01FEC", MsiGuid.Compress(g));
        Assert.Equal("{90120000-0030-0000-0000-0000000FF1CE}", MsiGuid.Expand("00002109030000000000000000F01FEC"));
    }

    [Theory]
    [InlineData("90120000-0030-0000-0000-0000000FF1CE")]
    [InlineData("{not-a-guid}")]
    [InlineData("")]
    public void OnlyBracedGuidsParse(string text) => Assert.False(MsiGuid.TryParseBraced(text, out _));

    [Theory]
    [InlineData("zz")]
    [InlineData("0000210903000000000000000F01FEC")]
    [InlineData("00002109030000000000000000F01FEG")]
    public void BadPackedCodesDoNotExpand(string packed) => Assert.Null(MsiGuid.Expand(packed));

    [Theory]
    [InlineData("Tools", "Acme Widget Tools")]
    [InlineData("Update", "Google Update")]
    [InlineData("Common Files", "Acme Common Files")]
    [InlineData("Microsoft", "Microsoft Edge")]
    [InlineData("x64", "Acme x64")]
    [InlineData("App", "App")]
    public void GenericNamePartsNeverMatch(string candidate, string product) =>
        Assert.Equal(NameMatch.None, NameMatcher.Match(candidate, product));

    [Theory]
    [InlineData("AcmeWidget", "Acme Widget", NameMatch.Exact)]
    [InlineData("Acme Widget 2.4.1", "Acme Widget", NameMatch.Exact)]
    [InlineData("Widget", "Acme Widget", NameMatch.Partial)]
    [InlineData("Notepad++", "Notepad++ (64-bit x64)", NameMatch.Exact)]
    [InlineData("Paint", "Acme Widget", NameMatch.None)]
    public void NameMatchLevels(string candidate, string product, NameMatch expected) =>
        Assert.Equal(expected, NameMatcher.Match(candidate, product));

    [Theory]
    [InlineData("NVIDIA Corporation", "NVIDIA Corporation", true)]
    [InlineData("NVIDIA", "NVIDIA Corporation", true)]
    [InlineData("Microsoft", "Microsoft Corporation", true)]
    [InlineData("Mozilla", "Acme Inc.", false)]
    public void PublisherMatching(string candidate, string publisher, bool expected) =>
        Assert.Equal(expected, NameMatcher.MatchesPublisher(candidate, publisher));

    [Theory]
    [InlineData("NVIDIA Corporation", true)]
    [InlineData("Microsoft Corporation", true)]
    [InlineData("Adobe", true)]
    [InlineData("Advanced Micro Devices, Inc.", true)]
    [InlineData("Mozilla", false)]
    public void SharedPublishers(string name, bool expected) =>
        Assert.Equal(expected, NameMatcher.IsSharedPublisher(name));

    [Fact]
    public void SingleAnchorIsLowEvenWithHighScore()
    {
        var (score, anchors, tier) = Confidence.Evaluate([Confidence.InstallDirExact(@"C:\X")]);
        Assert.Equal(20, score);
        Assert.Equal(1, anchors);
        Assert.Equal(ConfidenceTier.Low, tier);
    }

    [Fact]
    public void SameAnchorTwiceStillCountsOnce()
    {
        var (_, anchors, tier) = Confidence.Evaluate([Confidence.InstallDirExact(@"C:\X"), Confidence.InsideInstallDir(@"C:\X\a"), Confidence.ReferencesInstallDir("a")]);
        Assert.Equal(1, anchors);
        Assert.Equal(ConfidenceTier.Low, tier);
    }

    [Fact]
    public void TwoAnchorsHighAndMediumThresholds()
    {
        Assert.Equal(ConfidenceTier.High, Confidence.Evaluate([Confidence.InstallDirExact(@"C:\X"), Confidence.NameExact("X")]).Tier);
        Assert.Equal(ConfidenceTier.Medium, Confidence.Evaluate([Confidence.PublisherMatch("Acme"), Confidence.NameExact("X")]).Tier);
        Assert.Equal(ConfidenceTier.Low, Confidence.Evaluate([Confidence.PublisherMatch("Acme"), Confidence.NamePartial("X")]).Tier);
    }

    [Fact]
    public void OtherProgramNameRemovesNameAnchor()
    {
        var ev = new[] { Confidence.PublisherMatch("Acme"), Confidence.NameExact("Widget"), Confidence.OtherProgramName("Widget Pro") };
        var (_, anchors, tier) = Confidence.Evaluate(ev);
        Assert.Equal(1, anchors);
        Assert.Equal(ConfidenceTier.Low, tier);
    }

    [Fact]
    public void ReasonLineIsNeverEmpty()
    {
        var ev = new[] { Confidence.InstallDirExact(@"C:\X"), Confidence.NameExact("X") };
        var (_, anchors, tier) = Confidence.Evaluate(ev);
        Assert.False(string.IsNullOrWhiteSpace(Confidence.Reason(ev, anchors, tier)));
        Assert.False(string.IsNullOrWhiteSpace(Confidence.Reason([], 0, ConfidenceTier.Low)));
    }
}
