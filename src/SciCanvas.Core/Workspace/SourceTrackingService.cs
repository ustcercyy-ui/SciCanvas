using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Workspace;

public sealed record SourceTrackingResult(
    SourceLinkState State,
    bool HashMatches,
    bool DimensionsMatch,
    bool ModifiedTimeMatches,
    IReadOnlyList<string> Warnings);

public sealed record RelinkResult(
    ScientificAsset Asset,
    SourceTrackingResult Validation,
    bool RequiresScientificReview);

public static class SourceTrackingService
{
    public static SourceTrackingResult Verify(
        ScientificAsset asset,
        SourceFingerprint? currentFingerprint,
        ImageMetadata? currentMetadata)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (currentFingerprint is null || currentMetadata is null)
        {
            return new SourceTrackingResult(
                SourceLinkState.Missing,
                false,
                false,
                false,
                ["Source file is missing." ]);
        }

        bool hashMatches = string.Equals(
            asset.Source.Fingerprint.Sha256,
            currentFingerprint.Sha256,
            StringComparison.OrdinalIgnoreCase);
        bool dimensionsMatch =
            asset.Image.PixelSize.Width == currentMetadata.PixelSize.Width &&
            asset.Image.PixelSize.Height == currentMetadata.PixelSize.Height;
        bool modifiedTimeMatches =
            asset.Source.Fingerprint.LastWriteTimeUtc == currentFingerprint.LastWriteTimeUtc;
        List<string> warnings = [];
        if (!hashMatches)
        {
            warnings.Add("Source content hash changed since import." );
        }

        if (!dimensionsMatch)
        {
            warnings.Add("Source dimensions changed since import." );
        }

        if (!modifiedTimeMatches && hashMatches)
        {
            warnings.Add("Source modified time changed but content hash is identical." );
        }

        SourceLinkState state = hashMatches
            ? SourceLinkState.Verified
            : SourceLinkState.Modified;
        return new SourceTrackingResult(
            state,
            hashMatches,
            dimensionsMatch,
            modifiedTimeMatches,
            warnings);
    }

    public static RelinkResult Relink(
        ScientificAsset asset,
        string replacementPath,
        SourceFingerprint replacementFingerprint,
        ImageMetadata replacementMetadata,
        SpatialCalibration? replacementCalibration = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementPath);
        SourceTrackingResult validation = Verify(asset, replacementFingerprint, replacementMetadata);
        bool requiresReview = !validation.HashMatches ||
                              !validation.DimensionsMatch ||
                              !EqualsCalibration(asset.Calibration, replacementCalibration);
        AssetSourceReference source = validation.HashMatches
            ? asset.Source with
            {
                Path = Path.GetFullPath(replacementPath),
                FileName = Path.GetFileName(replacementPath),
                Fingerprint = replacementFingerprint,
            }
            : asset.Source.NextRevision(
                Path.GetFullPath(replacementPath),
                Path.GetFileName(replacementPath),
                replacementFingerprint);
        ScientificAsset relinked = asset with
        {
            Source = source,
            Image = replacementMetadata,
            Calibration = replacementCalibration,
            LinkState = validation.HashMatches ? SourceLinkState.Relocated : SourceLinkState.Modified,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return new RelinkResult(relinked, validation, requiresReview);
    }

    private static bool EqualsCalibration(
        SpatialCalibration? first,
        SpatialCalibration? second) =>
        first is null && second is null ||
        first is not null && second is not null &&
        first.UnitsPerPixelX.Equals(second.UnitsPerPixelX) &&
        first.UnitsPerPixelY.Equals(second.UnitsPerPixelY) &&
        string.Equals(first.Unit, second.Unit, StringComparison.OrdinalIgnoreCase) &&
        first.Origin == second.Origin;
}
