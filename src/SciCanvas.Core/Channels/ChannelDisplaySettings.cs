using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Channels;

/// <summary>
/// Display-only mapping for one scientific channel. Raw samples are never rewritten by
/// this type and must remain the input to scientific analysis.
/// </summary>
public sealed record ChannelDisplaySettings(
    Guid ChannelId,
    bool Visible,
    string Color,
    double Opacity,
    double DisplayMinimum,
    double DisplayMaximum,
    double Gamma,
    bool Invert,
    string Colormap = "viridis")
{
    public ChannelDisplaySettings EnsureValid()
    {
        if (ChannelId == Guid.Empty || !ScientificStyleColor.ValidateColor(Color) ||
            !double.IsFinite(Opacity) || Opacity is < 0 or > 1 ||
            !double.IsFinite(DisplayMinimum) || !double.IsFinite(DisplayMaximum) ||
            DisplayMaximum <= DisplayMinimum ||
            !double.IsFinite(Gamma) || Gamma is <= 0 or > 100 ||
            !ScientificColormap.IsSupported(Colormap))
        {
            throw new InvalidOperationException(
                "通道显示设置必须包含有效颜色、透明度、递增显示范围、正 Gamma 和受支持的 colormap。");
        }

        return this;
    }

    public double NormalizeRawValue(double rawValue)
    {
        EnsureValid();
        if (!double.IsFinite(rawValue))
        {
            throw new ArgumentOutOfRangeException(nameof(rawValue), "原始通道值必须是有限数值。");
        }

        double normalized = Math.Clamp(
            (rawValue - DisplayMinimum) / (DisplayMaximum - DisplayMinimum),
            0,
            1);
        normalized = Math.Pow(normalized, 1 / Gamma);
        return Invert ? 1 - normalized : normalized;
    }

    public static ChannelDisplaySettings CreateDefault(ScientificChannelDescriptor channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        channel.EnsureValid();
        double maximum = Math.Pow(2, channel.BitDepth) - 1;
        return new ChannelDisplaySettings(
            channel.Id,
            Visible: true,
            channel.DefaultColor,
            Opacity: 1,
            DisplayMinimum: 0,
            DisplayMaximum: maximum,
            Gamma: 1,
            Invert: false,
            Colormap: "viridis");
    }
}
