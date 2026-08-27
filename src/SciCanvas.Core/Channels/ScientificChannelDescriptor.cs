using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Channels;

public enum ScientificChannelSourceKind
{
    InterleavedComponent,
    FramePlane,
    ExternalAsset,
}

public enum ScientificSampleType
{
    UInt8,
    UInt16,
}

public sealed record ScientificChannelDescriptor(
    Guid Id,
    int Index,
    string Name,
    ScientificChannelSourceKind SourceKind,
    ScientificSampleType SampleType,
    int BitDepth,
    string? Unit = null,
    string? Role = null,
    string DefaultColor = "#FFFFFFFF")
{
    public ScientificChannelDescriptor EnsureValid()
    {
        bool bitDepthMatches = SampleType switch
        {
            ScientificSampleType.UInt8 => BitDepth is >= 1 and <= 8,
            ScientificSampleType.UInt16 => BitDepth is >= 9 and <= 16,
            _ => false,
        };
        if (Id == Guid.Empty || Index < 0 || string.IsNullOrWhiteSpace(Name) || Name.Length > 128 ||
            !Enum.IsDefined(SourceKind) || !Enum.IsDefined(SampleType) || !bitDepthMatches ||
            Unit?.Length > 64 || Role?.Length > 128 || !ScientificStyleColor.ValidateColor(DefaultColor))
        {
            throw new InvalidOperationException("科研通道描述必须包含有效 ID、索引、名称、样本类型、位深和默认颜色。");
        }

        return this;
    }
}
