using SciCanvas.Core.Export;
using SciCanvas.Core.Sources;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class WindowsPathSafetyPolicyTests
{
    [Fact]
    public async Task RejectsExactSourcePath()
    {
        using TestWorkspace workspace = new();
        string sourcePath = workspace.CreateFile("source.tif", new byte[] { 1, 2, 3, 4 });
        ReadOnlySourceAssetReader reader = new(new FakeMetadataProbe());
        SourceAsset source = await reader.ImportAsync(sourcePath);
        WindowsPathSafetyPolicy policy = new();

        ExportPathDecision decision = await policy.ValidateExportTargetAsync(
            sourcePath,
            new[] { source });

        Assert.False(decision.IsAllowed);
        Assert.Equal(ExportPathRejectionReason.SameAsSourcePath, decision.RejectionReason);
    }

    [Fact]
    public async Task AllowsDifferentNewOutputPath()
    {
        using TestWorkspace workspace = new();
        string sourcePath = workspace.CreateFile("source.tif", new byte[] { 1, 2, 3, 4 });
        ReadOnlySourceAssetReader reader = new(new FakeMetadataProbe());
        SourceAsset source = await reader.ImportAsync(sourcePath);
        string targetPath = Path.Combine(workspace.Root, "source_export.tif");
        WindowsPathSafetyPolicy policy = new();

        ExportPathDecision decision = await policy.ValidateExportTargetAsync(
            targetPath,
            new[] { source });

        Assert.True(decision.IsAllowed);
        Assert.Equal(Path.GetFullPath(targetPath), decision.NormalizedTargetPath);
    }
}

