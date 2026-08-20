using System.Security.Cryptography;
using SciCanvas.Core.Sources;

namespace SciCanvas.Platform.Windows;

public sealed class ReadOnlySourceAssetReader : ISourceAssetReader
{
    private const int BufferSize = 128 * 1024;

    private readonly IImageMetadataProbe _metadataProbe;
    private readonly WindowsFileIdentityProvider _fileIdentityProvider;

    public ReadOnlySourceAssetReader(
        IImageMetadataProbe metadataProbe,
        WindowsFileIdentityProvider? fileIdentityProvider = null)
    {
        _metadataProbe = metadataProbe ?? throw new ArgumentNullException(nameof(metadataProbe));
        _fileIdentityProvider = fileIdentityProvider ?? new WindowsFileIdentityProvider();
    }

    public async Task<SourceAsset> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string fullPath = NormalizeExistingFile(path);
        FileInfo before = new(fullPath);

        await using FileStream source = OpenReadOnly(fullPath);
        string? windowsFileId = _fileIdentityProvider.TryGetFileId(source.SafeFileHandle);
        byte[] hash = await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false);

        source.Position = 0;
        var metadata = await _metadataProbe
            .ProbeAsync(source, Path.GetFileName(fullPath), cancellationToken)
            .ConfigureAwait(false);

        before.Refresh();
        if (before.Length != source.Length)
        {
            throw new IOException("源文件在导入过程中发生变化，请等待文件写入完成后重试。");
        }

        SourceFingerprint fingerprint = new(
            source.Length,
            new DateTimeOffset(before.LastWriteTimeUtc),
            Convert.ToHexString(hash),
            windowsFileId);

        return new SourceAsset(
            Guid.NewGuid(),
            Path.GetFileName(fullPath),
            fullPath,
            fingerprint,
            metadata,
            SourceLinkState.Verified);
    }

    public async Task<SourceVerification> VerifyAsync(
        SourceAsset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!File.Exists(asset.OriginalPath))
        {
            return new SourceVerification(
                SourceLinkState.Missing,
                null,
                "找不到源文件，请重新链接。");
        }

        SourceFingerprint current = await ComputeFingerprintAsync(
            asset.OriginalPath,
            cancellationToken).ConfigureAwait(false);

        if (string.Equals(
                current.Sha256,
                asset.Fingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new SourceVerification(SourceLinkState.Verified, current, "源文件未发生变化。");
        }

        return new SourceVerification(
            SourceLinkState.Modified,
            current,
            "源文件内容已经改变，SciCanvas不会静默接受新版本。");
    }

    private async Task<SourceFingerprint> ComputeFingerprintAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string fullPath = NormalizeExistingFile(path);
        FileInfo info = new(fullPath);

        await using FileStream source = OpenReadOnly(fullPath);
        string? windowsFileId = _fileIdentityProvider.TryGetFileId(source.SafeFileHandle);
        byte[] hash = await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false);
        info.Refresh();

        return new SourceFingerprint(
            source.Length,
            new DateTimeOffset(info.LastWriteTimeUtc),
            Convert.ToHexString(hash),
            windowsFileId);
    }

    private static FileStream OpenReadOnly(string fullPath)
    {
        return new FileStream(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = BufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
    }

    private static string NormalizeExistingFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到源图像。", fullPath);
        }

        return fullPath;
    }
}

