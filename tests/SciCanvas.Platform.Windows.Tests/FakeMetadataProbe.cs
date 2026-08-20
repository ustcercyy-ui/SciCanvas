using System.IO;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;

namespace SciCanvas.Platform.Windows.Tests;

internal sealed class FakeMetadataProbe : IImageMetadataProbe
{
    public ValueTask<ImageMetadata> ProbeAsync(
        Stream source,
        string fileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ImageMetadata(
            new PixelSize64(2048, 1536),
            3,
            16,
            "rgb16"));
    }
}
