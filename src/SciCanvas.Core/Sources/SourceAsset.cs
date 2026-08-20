using SciCanvas.Core.Images;

namespace SciCanvas.Core.Sources;

public sealed record SourceAsset(
    Guid Id,
    string DisplayName,
    string OriginalPath,
    SourceFingerprint Fingerprint,
    ImageMetadata Metadata,
    SourceLinkState LinkState);

