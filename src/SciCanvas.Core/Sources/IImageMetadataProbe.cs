using SciCanvas.Core.Images;

namespace SciCanvas.Core.Sources;

public interface IImageMetadataProbe
{
    ValueTask<ImageMetadata> ProbeAsync(
        Stream source,
        string fileName,
        CancellationToken cancellationToken);
}

