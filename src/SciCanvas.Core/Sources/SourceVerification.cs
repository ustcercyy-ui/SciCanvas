namespace SciCanvas.Core.Sources;

public sealed record SourceVerification(
    SourceLinkState State,
    SourceFingerprint? CurrentFingerprint,
    string? Message);

