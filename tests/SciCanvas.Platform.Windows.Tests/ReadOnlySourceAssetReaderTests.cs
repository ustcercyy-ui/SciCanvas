using System.Security.Cryptography;
using SciCanvas.Core.Sources;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ReadOnlySourceAssetReaderTests
{
    [Fact]
    public async Task ImportAsync_DoesNotChangeSourceBytes()
    {
        using TestWorkspace workspace = new();
        byte[] contents = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        string path = workspace.CreateFile("source.tif", contents);
        string hashBefore = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        ReadOnlySourceAssetReader reader = new(new FakeMetadataProbe());

        SourceAsset asset = await reader.ImportAsync(path);

        string hashAfter = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        Assert.Equal(hashBefore, hashAfter);
        Assert.Equal(hashBefore, asset.Fingerprint.Sha256);
        Assert.Equal(SourceLinkState.Verified, asset.LinkState);
        Assert.Equal(Path.GetFullPath(path), asset.OriginalPath);
    }

    [Fact]
    public async Task VerifyAsync_DetectsExternalModification()
    {
        using TestWorkspace workspace = new();
        string path = workspace.CreateFile("source.tif", new byte[] { 1, 2, 3, 4 });
        ReadOnlySourceAssetReader reader = new(new FakeMetadataProbe());
        SourceAsset asset = await reader.ImportAsync(path);
        await File.WriteAllBytesAsync(path, new byte[] { 9, 8, 7, 6 });

        SourceVerification verification = await reader.VerifyAsync(asset);

        Assert.Equal(SourceLinkState.Modified, verification.State);
        Assert.NotNull(verification.CurrentFingerprint);
    }
}

