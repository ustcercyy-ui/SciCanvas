namespace SciCanvas.Core.Sources;

public interface ISourceAssetReader
{
    Task<SourceAsset> ImportAsync(string path, CancellationToken cancellationToken = default);

    Task<SourceVerification> VerifyAsync(
        SourceAsset asset,
        CancellationToken cancellationToken = default);
}

