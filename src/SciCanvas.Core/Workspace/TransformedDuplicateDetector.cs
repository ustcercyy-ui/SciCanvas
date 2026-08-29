using SciCanvas.Core.Channels;

namespace SciCanvas.Core.Workspace;

/// <summary>The eight exact symmetries of a rectangular raw-pixel crop.</summary>
public enum RawCropTransform
{
    Identity,
    Rotate90,
    Rotate180,
    Rotate270,
    MirrorX,
    MirrorY,
    MirrorMainDiagonal,
    MirrorAntiDiagonal,
}

public sealed record RawCropQcCandidate(
    ImagePlane Plane,
    QcIssueLocation Location,
    string DisplayName)
{
    public RawCropQcCandidate EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(Plane);
        ArgumentNullException.ThrowIfNull(Location);
        if (string.IsNullOrWhiteSpace(DisplayName) || Location.PanelId is null)
        {
            throw new InvalidOperationException("Raw crop QC candidate 必须包含显示名称和 Panel location。");
        }

        return this;
    }
}

public sealed record TransformedDuplicateMatch(
    RawCropQcCandidate First,
    RawCropQcCandidate Second,
    RawCropTransform Transform)
{
    public IReadOnlyList<QcIssueLocation> Locations => [First.Location, Second.Location];
}

/// <summary>
/// Deterministic, explainable D4 equality over the original typed samples.  It
/// never uses previews, display ranges, pseudocolor, histograms, pHash or a
/// down-converted buffer.
/// </summary>
public static class TransformedDuplicateDetector
{
    private static readonly RawCropTransform[] TransformOrder =
    [
        RawCropTransform.Identity,
        RawCropTransform.Rotate90,
        RawCropTransform.Rotate180,
        RawCropTransform.Rotate270,
        RawCropTransform.MirrorX,
        RawCropTransform.MirrorY,
        RawCropTransform.MirrorMainDiagonal,
        RawCropTransform.MirrorAntiDiagonal,
    ];

    public static IReadOnlyList<TransformedDuplicateMatch> FindExactDuplicates(
        IEnumerable<RawCropQcCandidate> candidates)
    {
        RawCropQcCandidate[] items = (candidates ?? throw new ArgumentNullException(nameof(candidates)))
            .Select(candidate => candidate.EnsureValid())
            .ToArray();
        var matches = new List<TransformedDuplicateMatch>();
        for (int firstIndex = 0; firstIndex < items.Length; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1; secondIndex < items.Length; secondIndex++)
            {
                RawCropTransform? transform = FindTransform(items[firstIndex].Plane, items[secondIndex].Plane);
                if (transform is not null)
                {
                    matches.Add(new TransformedDuplicateMatch(
                        items[firstIndex],
                        items[secondIndex],
                        transform.Value));
                }
            }
        }

        return Array.AsReadOnly(matches.ToArray());
    }

    public static RawCropTransform? FindTransform(ImagePlane first, ImagePlane second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.SampleType != second.SampleType || first.BitDepth != second.BitDepth)
        {
            return null;
        }

        foreach (RawCropTransform transform in TransformOrder)
        {
            (int width, int height) = GetTransformedSize(first.Width, first.Height, transform);
            if (width != second.Width || height != second.Height)
            {
                continue;
            }

            bool equal = first.RawSamples switch
            {
                UInt8ImagePlaneSamples first8 when second.RawSamples is UInt8ImagePlaneSamples second8 =>
                    EqualsTransformed(first8, first.Width, first.Height, second8, transform),
                UInt16ImagePlaneSamples first16 when second.RawSamples is UInt16ImagePlaneSamples second16 =>
                    EqualsTransformed(first16, first.Width, first.Height, second16, transform),
                _ => false,
            };
            if (equal)
            {
                return transform;
            }
        }

        return null;
    }

    private static bool EqualsTransformed<T>(
        IReadOnlyList<T> first,
        int sourceWidth,
        int sourceHeight,
        IReadOnlyList<T> second,
        RawCropTransform transform)
        where T : IEquatable<T>
    {
        (int outputWidth, int outputHeight) = GetTransformedSize(sourceWidth, sourceHeight, transform);
        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                (int sourceX, int sourceY) = MapOutputToSource(
                    x,
                    y,
                    sourceWidth,
                    sourceHeight,
                    transform);
                if (!first[checked(sourceY * sourceWidth + sourceX)]
                        .Equals(second[checked(y * outputWidth + x)]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static (int Width, int Height) GetTransformedSize(
        int width,
        int height,
        RawCropTransform transform) => transform is
            RawCropTransform.Rotate90 or RawCropTransform.Rotate270 or
            RawCropTransform.MirrorMainDiagonal or RawCropTransform.MirrorAntiDiagonal
                ? (height, width)
                : (width, height);

    private static (int X, int Y) MapOutputToSource(
        int x,
        int y,
        int sourceWidth,
        int sourceHeight,
        RawCropTransform transform) => transform switch
        {
            RawCropTransform.Identity => (x, y),
            RawCropTransform.Rotate90 => (y, sourceHeight - 1 - x),
            RawCropTransform.Rotate180 => (sourceWidth - 1 - x, sourceHeight - 1 - y),
            RawCropTransform.Rotate270 => (sourceWidth - 1 - y, x),
            RawCropTransform.MirrorX => (x, sourceHeight - 1 - y),
            RawCropTransform.MirrorY => (sourceWidth - 1 - x, y),
            RawCropTransform.MirrorMainDiagonal => (y, x),
            RawCropTransform.MirrorAntiDiagonal =>
                (sourceWidth - 1 - y, sourceHeight - 1 - x),
            _ => throw new ArgumentOutOfRangeException(nameof(transform)),
        };
}
