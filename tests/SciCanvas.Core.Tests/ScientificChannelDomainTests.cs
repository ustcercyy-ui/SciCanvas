using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Tests;

public sealed class ScientificChannelDomainTests
{
    [Fact]
    public void DisplaySettings_NormalizeRawValueWithoutMutatingTypedPlane()
    {
        ScientificChannelDescriptor channel = CreateChannel(
            new Guid("11111111-1111-1111-1111-111111111111"),
            0,
            "DAPI",
            ScientificSampleType.UInt16,
            16,
            "#FF4FC3F7");
        ushort[] sourceValues = [1000, 4000];
        var plane = new ImagePlane(
            Guid.NewGuid(),
            sourceRevision: 7,
            frameIndex: 0,
            new PixelRect64(0, 0, 2, 1),
            channel,
            new UInt16ImagePlaneSamples(sourceValues));
        sourceValues[0] = 65_535;
        var settings = new ChannelDisplaySettings(
            channel.Id,
            Visible: true,
            Color: channel.DefaultColor,
            Opacity: 1,
            DisplayMinimum: 0,
            DisplayMaximum: 4000,
            Gamma: 2,
            Invert: false);

        Assert.Equal(0.5, settings.NormalizeRawValue(1000), 12);
        Assert.Equal(0, (settings with { Invert = true }).NormalizeRawValue(4000), 12);
        ScientificChannelCompositeResult composite = ScientificChannelComposite.Compose(
            [new ScientificChannelCompositeInput(plane, settings)]);
        Assert.Equal(0.5 * 79 / 255, composite[0, 0].Red, 12);
        Assert.Equal(0.5 * 195 / 255, composite[0, 0].Green, 12);
        Assert.Equal(0.5 * 247 / 255, composite[0, 0].Blue, 12);
        Assert.Equal(1000, plane.GetRawValue(0, 0));
        UInt16ImagePlaneSamples raw = Assert.IsType<UInt16ImagePlaneSamples>(plane.RawSamples);
        Assert.Equal(new ushort[] { 1000, 4000 }, raw);
        Assert.Equal(7, plane.SourceRevision);
    }

    [Fact]
    public void Compose_UsesDeterministicAdditiveMathIndependentOfInputOrder()
    {
        ScientificChannelDescriptor red = CreateChannel(
            new Guid("22222222-2222-2222-2222-222222222222"), 0, "Ti",
            ScientificSampleType.UInt8, 8, "#FFFF0000");
        ScientificChannelDescriptor green = CreateChannel(
            new Guid("33333333-3333-3333-3333-333333333333"), 0, "Al",
            ScientificSampleType.UInt8, 8, "#FF00FF00");
        Guid assetId = Guid.NewGuid();
        var redPlane = new ImagePlane(
            assetId, 1, 0, new PixelRect64(0, 0, 1, 1), red,
            new UInt8ImagePlaneSamples([50]));
        var greenPlane = new ImagePlane(
            assetId, 1, 0, new PixelRect64(0, 0, 1, 1), green,
            new UInt8ImagePlaneSamples([25]));
        var redSettings = new ChannelDisplaySettings(red.Id, true, red.DefaultColor, 1, 0, 100, 1, false);
        var greenSettings = new ChannelDisplaySettings(green.Id, true, green.DefaultColor, 1, 0, 100, 1, false);

        ScientificChannelCompositeResult forward = ScientificChannelComposite.Compose(
        [
            new ScientificChannelCompositeInput(redPlane, redSettings),
            new ScientificChannelCompositeInput(greenPlane, greenSettings),
        ]);
        ScientificChannelCompositeResult reverse = ScientificChannelComposite.Compose(
        [
            new ScientificChannelCompositeInput(greenPlane, greenSettings),
            new ScientificChannelCompositeInput(redPlane, redSettings),
        ]);

        Assert.Equal(forward.Pixels, reverse.Pixels);
        Assert.Equal(0.5, forward[0, 0].Red, 12);
        Assert.Equal(0.25, forward[0, 0].Green, 12);
        Assert.Equal(0, forward[0, 0].Blue);
        Assert.Equal(50, redPlane.GetRawValue(0, 0));
        Assert.Equal(25, greenPlane.GetRawValue(0, 0));
    }

    [Fact]
    public void Compose_ClampsAdditiveChannelsAndSkipsHiddenChannel()
    {
        ScientificChannelDescriptor first = CreateChannel(
            new Guid("44444444-4444-4444-4444-444444444444"), 0, "First",
            ScientificSampleType.UInt8, 8, "#FFFFFFFF");
        ScientificChannelDescriptor second = CreateChannel(
            new Guid("55555555-5555-5555-5555-555555555555"), 0, "Second",
            ScientificSampleType.UInt8, 8, "#FFFFFFFF");
        Guid assetId = Guid.NewGuid();
        var firstPlane = new ImagePlane(
            assetId, 1, 0, new PixelRect64(0, 0, 1, 1), first,
            new UInt8ImagePlaneSamples([255]));
        var secondPlane = new ImagePlane(
            assetId, 1, 0, new PixelRect64(0, 0, 1, 1), second,
            new UInt8ImagePlaneSamples([255]));

        ScientificChannelCompositeResult saturated = ScientificChannelComposite.Compose(
        [
            new(firstPlane, ChannelDisplaySettings.CreateDefault(first)),
            new(secondPlane, ChannelDisplaySettings.CreateDefault(second)),
        ]);
        ScientificChannelCompositeResult hidden = ScientificChannelComposite.Compose(
        [
            new(firstPlane, ChannelDisplaySettings.CreateDefault(first) with { Visible = false }),
            new(secondPlane, ChannelDisplaySettings.CreateDefault(second) with { Visible = false }),
        ]);

        Assert.Equal(new ScientificDisplayPixel(1, 1, 1), saturated[0, 0]);
        Assert.Equal(new ScientificDisplayPixel(0, 0, 0), hidden[0, 0]);
    }

    [Fact]
    public void DescriptorAndDisplaySettings_RejectInvalidScientificSemantics()
    {
        ScientificChannelDescriptor mismatchedBitDepth = CreateChannel(
            Guid.NewGuid(), 0, "Invalid", ScientificSampleType.UInt8, 16, "#FFFFFFFF");
        Assert.Throws<InvalidOperationException>(() => mismatchedBitDepth.EnsureValid());

        ScientificChannelDescriptor valid = CreateChannel(
            Guid.NewGuid(), 0, "Valid", ScientificSampleType.UInt8, 8, "#FFFFFFFF");
        var invalidDisplay = new ChannelDisplaySettings(
            valid.Id, true, valid.DefaultColor, 1, 100, 100, 1, false);
        Assert.Throws<InvalidOperationException>(() => invalidDisplay.EnsureValid());
    }

    private static ScientificChannelDescriptor CreateChannel(
        Guid id,
        int index,
        string name,
        ScientificSampleType sampleType,
        int bitDepth,
        string color) => new(
        id,
        index,
        name,
        ScientificChannelSourceKind.InterleavedComponent,
        sampleType,
        bitDepth,
        DefaultColor: color);
}
