using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Channels;

public readonly record struct ScientificDisplayPixel(
    double Red,
    double Green,
    double Blue,
    double Alpha = 1)
{
    public byte Red8 => ToByte(Red);

    public byte Green8 => ToByte(Green);

    public byte Blue8 => ToByte(Blue);

    public byte Alpha8 => ToByte(Alpha);

    private static byte ToByte(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0, 1) * byte.MaxValue);
}

public sealed record ScientificChannelCompositeInput(
    ImagePlane Plane,
    ChannelDisplaySettings DisplaySettings,
    RegisteredPlaneResamplingResult? RegisteredDisplayPlane = null)
{
    public int Width => RegisteredDisplayPlane?.Width ?? Plane.Width;

    public int Height => RegisteredDisplayPlane?.Height ?? Plane.Height;

    public double GetValue(int index) =>
        RegisteredDisplayPlane?.GetValue(index) ?? Plane.RawSamples.GetValue(index);

    public bool IsValid(int index) =>
        RegisteredDisplayPlane?.IsValid(index) ?? true;
}

public sealed record ScientificChannelCompositeResult(
    int Width,
    int Height,
    IReadOnlyList<ScientificDisplayPixel> Pixels)
{
    public ScientificDisplayPixel this[int x, int y] => Pixels[checked(y * Width + x)];
}

/// <summary>Deterministic display-only additive composite. It never mutates or replaces raw planes.</summary>
public static class ScientificChannelComposite
{
    public static ScientificChannelCompositeResult Compose(
        IReadOnlyCollection<ScientificChannelCompositeInput> inputs) =>
        ComposeHighPrecision(inputs);

    /// <summary>
    /// Composes normalized floating-point RGB without presentation quantization.
    /// 8-bit preview and 16-bit export quantize only at their final boundaries.
    /// </summary>
    public static ScientificChannelCompositeResult ComposeHighPrecision(
        IReadOnlyCollection<ScientificChannelCompositeInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            throw new ArgumentException("复合显示至少需要一个通道平面。", nameof(inputs));
        }

        ScientificChannelCompositeInput first = inputs.First();
        ArgumentNullException.ThrowIfNull(first.Plane);
        int width = first.Width;
        int height = first.Height;
        var prepared = new List<PreparedChannel>(inputs.Count);
        foreach (ScientificChannelCompositeInput input in inputs.OrderBy(item => item.Plane.Channel.Id))
        {
            ArgumentNullException.ThrowIfNull(input.Plane);
            ArgumentNullException.ThrowIfNull(input.DisplaySettings);
            input.Plane.Channel.EnsureValid();
            input.DisplaySettings.EnsureValid();
            if (input.Width != width || input.Height != height ||
                input.DisplaySettings.ChannelId != input.Plane.Channel.Id)
            {
                throw new InvalidOperationException("复合通道必须具有相同尺寸，且显示设置必须引用对应通道。");
            }

            if (input.RegisteredDisplayPlane is { } registered &&
                !ReferenceEquals(registered.SourcePlane, input.Plane))
            {
                throw new InvalidOperationException("配准显示缓冲区必须保留对应的原始通道平面。");
            }

            if (!input.DisplaySettings.Visible)
            {
                continue;
            }

            if (!ScientificStyleColor.TryParseColor(input.DisplaySettings.Color, out ScientificColorValue color))
            {
                throw new InvalidOperationException("复合通道颜色无效。");
            }

            prepared.Add(new PreparedChannel(input, input.DisplaySettings, color));
        }

        ScientificDisplayPixel[] pixels = new ScientificDisplayPixel[checked(width * height)];
        for (int index = 0; index < pixels.Length; index++)
        {
            double red = 0;
            double green = 0;
            double blue = 0;
            bool hasValidVisibleSample = false;
            foreach (PreparedChannel channel in prepared)
            {
                if (!channel.Input.IsValid(index))
                {
                    continue;
                }

                hasValidVisibleSample = true;
                double normalized = channel.Settings.NormalizeRawValue(channel.Input.GetValue(index));
                double amount = normalized * channel.Settings.Opacity * channel.Color.Alpha / byte.MaxValue;
                red += amount * channel.Color.Red / byte.MaxValue;
                green += amount * channel.Color.Green / byte.MaxValue;
                blue += amount * channel.Color.Blue / byte.MaxValue;
            }

            pixels[index] = new ScientificDisplayPixel(
                Math.Clamp(red, 0, 1),
                Math.Clamp(green, 0, 1),
                Math.Clamp(blue, 0, 1),
                prepared.Count == 0 || hasValidVisibleSample ? 1 : 0);
        }

        return new ScientificChannelCompositeResult(width, height, Array.AsReadOnly(pixels));
    }

    private sealed record PreparedChannel(
        ScientificChannelCompositeInput Input,
        ChannelDisplaySettings Settings,
        ScientificColorValue Color);
}
