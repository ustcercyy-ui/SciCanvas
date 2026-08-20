using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Tests;

public sealed class SourceFingerprintTests
{
    [Fact]
    public void Constructor_NormalizesSha256ToUppercase()
    {
        string hash = new('a', 64);

        SourceFingerprint fingerprint = new(10, DateTimeOffset.UnixEpoch, hash, null);

        Assert.Equal(new string('A', 64), fingerprint.Sha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void Constructor_RejectsInvalidSha256(string hash)
    {
        Assert.Throws<ArgumentException>(() =>
            new SourceFingerprint(10, DateTimeOffset.UnixEpoch, hash, null));
    }
}

