namespace SciCanvas.Core.Sources;

public sealed record SourceFingerprint
{
    public SourceFingerprint(
        long byteLength,
        DateTimeOffset lastWriteTimeUtc,
        string sha256,
        string? windowsFileId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);

        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 必须是64位十六进制字符串。", nameof(sha256));
        }

        ByteLength = byteLength;
        LastWriteTimeUtc = lastWriteTimeUtc;
        Sha256 = sha256.ToUpperInvariant();
        WindowsFileId = windowsFileId;
    }

    public long ByteLength { get; }

    public DateTimeOffset LastWriteTimeUtc { get; }

    public string Sha256 { get; }

    public string? WindowsFileId { get; }
}

