namespace SciCanvas.Core.Images;

/// <summary>
/// Normalized, audit-friendly subset of OME-XML. The source XML remains in the
/// original image; SciCanvas stores only fields required for scientific review.
/// </summary>
public sealed record OmeImageMetadata(
    string DimensionOrder,
    string PixelType,
    int SizeZ,
    int SizeC,
    int SizeT,
    double? PhysicalSizeX,
    double? PhysicalSizeY,
    double? PhysicalSizeZ,
    string? PhysicalSizeXUnit,
    string? PhysicalSizeYUnit,
    string? PhysicalSizeZUnit,
    double? TimeIncrement,
    string? TimeIncrementUnit,
    IReadOnlyList<string> ChannelNames,
    string XmlSha256)
{
    public int LogicalPlaneCount => checked(Math.Max(1, SizeZ) * Math.Max(1, SizeT));

    public string Summary =>
        $"OME {PixelType} · Z{Math.Max(1, SizeZ)} C{Math.Max(1, SizeC)} T{Math.Max(1, SizeT)} · {DimensionOrder}";
}
