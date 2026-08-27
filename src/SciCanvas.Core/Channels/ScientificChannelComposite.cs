using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Channels;

public readonly record struct ScientificDisplayPixel(double Red, double Green, double Blue)
{
    public byte Red8 => ToByte(Red);

    public byte Green8 => ToByte(Green);

    public byte Blue8 => ToByte(Blue);

    private static byte ToByte(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0, 1) * byte.MaxValue);
}

public sealed record ScientificChannelCompositeInput(
    ImagePlane Plane,
    ChannelDisplaySettings DisplaySettings);

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
        IReadOnlyCollection<ScientificChannelCompositeInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            throw new ArgumentException("复合显示至少需要一个通道平面。", nameof(inputs));
        }

        ScientificChannelCompositeInput first = inputs.First();
        ArgumentNullException.ThrowIfNull(first.Plane);
        int width = first.Plane.Width;
        int height = first.Plane.Height;
        var prepared = new List<PreparedChannel>(inputs.Count);
        foreach (ScientificChannelCompositeInput input in inputs.OrderBy(item => item.Plane.Channel.Id))
        {
            ArgumentNullException.ThrowIfNull(input.Plane);
            ArgumentNullException.ThrowIfNull(input.DisplaySettings);
            input.Plane.Channel.EnsureValid();
            input.DisplaySettings.EnsureValid();
            if (input.Plane.Width != width || input.Plane.Height != height ||
                input.DisplaySettings.ChannelId != input.Plane.Channel.Id)
            {
                throw new InvalidOperationException("复合通道必须具有相同尺寸，且显示设置必须引用对应通道。");
            }

            if (!input.DisplaySettings.Visible)
            {
                continue;
            }

            if (!ScientificStyleColor.TryParseColor(input.DisplaySettings.Color, out ScientificColorValue color))
            {
                throw new InvalidOperationException("复合通道颜色无效。");
            }

            prepared.Add(new PreparedChannel(input.Plane, input.DisplaySettings, color));
        }

        ScientificDisplayPixel[] pixels = new ScientificDisplayPixel[checked(width * height)];
        for (int index = 0; index < pixels.Length; index++)
        {
            double red = 0;
            double green = 0;
            double blue = 0;
            foreach (PreparedChannel channel in prepared)
            {
                double normalized = channel.Settings.NormalizeRawValue(channel.Plane.RawSamples.GetValue(index));
                double amount = normalized * channel.Settings.Opacity * channel.Color.Alpha / byte.MaxValue;
                red += amount * channel.Color.Red / byte.MaxValue;
                green += amount * channel.Color.Green / byte.MaxValue;
                blue += amount * channel.Color.Blue / byte.MaxValue;
            }

            pixels[index] = new ScientificDisplayPixel(
                Math.Clamp(red, 0, 1),
                Math.Clamp(green, 0, 1),
                Math.Clamp(blue, 0, 1));
        }

        return new ScientificChannelCompositeResult(width, height, Array.AsReadOnly(pixels));
    }

    private sealed record PreparedChannel(
        ImagePlane Plane,
        ChannelDisplaySettings Settings,
        ScientificColorValue Color);
}
