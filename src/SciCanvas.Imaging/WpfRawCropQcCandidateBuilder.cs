using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Imaging;

/// <summary>
/// Reads exact typed raw crops for explainable transformed-duplicate QC.
/// Display adjustments, LUTs, pseudocolor and resampled figure pixels are never used.
/// </summary>
public static class WpfRawCropQcCandidateBuilder
{
    public static IReadOnlyList<RawCropQcCandidate> Create(
        FigureExportDocument document,
        IReadOnlyDictionary<Guid, long>? sourceRevisions = null,
        Guid? figureId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyDictionary<Guid, long> revisions =
            sourceRevisions ?? new Dictionary<Guid, long>();
        var candidates = new List<RawCropQcCandidate>();
        foreach (FigurePanelExportItem panel in document.Panels.Where(panel =>
                     panel.IsVisible && panel.Source.Metadata.Channels == 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScientificSampleType sampleType = panel.Source.Metadata.BitsPerChannel <= 8
                ? ScientificSampleType.UInt8
                : ScientificSampleType.UInt16;
            if (panel.Source.Metadata.BitsPerChannel is < 1 or > 16)
            {
                continue;
            }

            var selector = new ScientificChannelDescriptor(
                panel.Source.Id,
                0,
                "raw",
                ScientificChannelSourceKind.ExternalAsset,
                sampleType,
                panel.Source.Metadata.BitsPerChannel);
            long revision = Math.Max(1, revisions.GetValueOrDefault(panel.Source.Id, 1));
            ImagePlane plane = WpfImagePlaneReader.ReadPlanes(
                panel.Source,
                panel.FrameIndex,
                panel.SourceRect,
                [selector],
                revision,
                cancellationToken)[0];
            candidates.Add(new RawCropQcCandidate(
                plane,
                new QcIssueLocation(
                    FigureId: figureId,
                    PanelId: panel.PanelId,
                    AssetId: panel.Source.Id,
                    SourceRegion: panel.SourceRect),
                string.IsNullOrWhiteSpace(panel.Label)
                    ? panel.Source.DisplayName
                    : panel.Label));
        }

        return candidates;
    }
}
