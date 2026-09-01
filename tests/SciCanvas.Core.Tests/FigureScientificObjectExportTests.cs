using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class FigureScientificObjectExportTests
{
    [Fact]
    public void EnsureValid_AcceptsAllCanonicalScientificObjectKinds()
    {
        foreach (FigureScientificObjectExportItem item in CreateCanonicalObjects())
        {
            item.EnsureValid(240, 160);
        }
    }

    [Fact]
    public void Preflight_FlagsInvalidScientificObjectWithObjectId()
    {
        Guid id = Guid.NewGuid();
        var invalidColorbar = new FigureScientificObjectExportItem(
            id,
            FigureScientificObjectKind.Colorbar,
            [new FigureScientificPoint(180, 20), new FigureScientificPoint(210, 120)],
            "Intensity",
            "#FFFFFFFF",
            "#FFFFFFFF",
            0,
            "#FFFFFFFF",
            "Arial",
            7,
            1,
            false,
            true,
            0,
            Minimum: 4,
            Maximum: 4,
            Unit: "a.u.");
        var document = new FigureExportDocument(
            240,
            160,
            300,
            [],
            scientificObjects: [invalidColorbar]);

        FigurePreflightResult result = FigurePreflight.Check(document, []);

        FigurePreflightIssue issue = Assert.Single(
            result.Issues,
            item => item.Code == "INVALID_SCIENTIFIC_OBJECT");
        Assert.Equal(id, issue.ObjectId);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void TypedColorbarSpec_RoundTripsCanonicalTicksBindingAndOrientation()
    {
        Guid channelId = Guid.NewGuid();
        IReadOnlyList<ColorbarTick> ticks = ColorbarObject.CreateDefaultTicks(10, 50, 3);
        var canonical = new ColorbarObject
        {
            Id = Guid.NewGuid(),
            Minimum = 10,
            Maximum = 50,
            Unit = "counts",
            Colormap = "magma",
            ChannelId = channelId,
            BindingState = ColorbarBindingState.Linked,
            Orientation = FigureObjectOrientation.Horizontal,
            Ticks = ticks,
        }.EnsureValid();
        var spec = new FigureColorbarExportSpec(
            canonical.Minimum,
            canonical.Maximum,
            canonical.Unit,
            canonical.Colormap,
            canonical.ChannelId,
            canonical.BindingState,
            canonical.Orientation,
            canonical.Ticks).EnsureValid();
        var item = new FigureScientificObjectExportItem(
            canonical.Id,
            FigureScientificObjectKind.Colorbar,
            [new FigureScientificPoint(20, 20), new FigureScientificPoint(180, 50)],
            "Intensity",
            "#FFFFFFFF",
            "#FFFFFFFF",
            0,
            "#FFFFFFFF",
            "Arial",
            7,
            1,
            false,
            true,
            0,
            Minimum: spec.Minimum,
            Maximum: spec.Maximum,
            Unit: spec.Unit,
            Colormap: spec.Colormap,
            ChannelId: spec.ChannelId,
            Colorbar: spec);

        item.EnsureValid(240, 160);

        Assert.Equal(FigureObjectOrientation.Horizontal, item.EffectiveColorbar!.Orientation);
        Assert.Equal(ColorbarBindingState.Linked, item.EffectiveColorbar.BindingState);
        Assert.Equal(["10", "30", "50"], item.EffectiveColorbar.Ticks.Select(tick => tick.Label));
    }

    [Fact]
    public void TypedChannelLegendSpec_CarriesItemsTypographyContainerAndPadding()
    {
        Guid firstChannel = Guid.NewGuid();
        var spec = new FigureChannelLegendExportSpec(
            [
                new FigureChannelLegendEntry("DAPI", "#FF4FC3F7", firstChannel),
                new FigureChannelLegendEntry("GFP", "#FF66BB6A"),
            ],
            "Arial",
            8,
            true,
            "#FFEEEEEE",
            "#FF101010",
            75,
            "#FF808080",
            1.5,
            9).EnsureValid();
        var item = new FigureScientificObjectExportItem(
            Guid.NewGuid(),
            FigureScientificObjectKind.ChannelLegend,
            [new FigureScientificPoint(20, 20), new FigureScientificPoint(160, 100)],
            "Channels",
            spec.BorderColor,
            spec.BackgroundColor,
            spec.BackgroundOpacityPercent,
            spec.TextColor,
            spec.FontFamily,
            spec.FontSizePt,
            spec.BorderWidthPt,
            spec.IsBold,
            true,
            0,
            ChannelLegendEntries: spec.Items,
            ChannelLegend: spec);

        item.EnsureValid(240, 160);

        Assert.Equal(9, item.EffectiveChannelLegend!.PaddingPixels);
        Assert.Equal(firstChannel, item.EffectiveChannelLegend.Items[0].ChannelId);
        Assert.Equal("#FF101010", item.EffectiveChannelLegend.BackgroundColor);
    }

    private static IReadOnlyList<FigureScientificObjectExportItem> CreateCanonicalObjects() =>
    [
        new(
            Guid.NewGuid(),
            FigureScientificObjectKind.PolygonAnnotation,
            [new FigureScientificPoint(20, 20), new FigureScientificPoint(90, 20), new FigureScientificPoint(70, 80)],
            "Membrane",
            "#FFFFB300",
            "#FFFFB300",
            12,
            "#FFFFFFFF",
            "Arial",
            7,
            1.25,
            true,
            true,
            0),
        new(
            Guid.NewGuid(),
            FigureScientificObjectKind.DirectionMarker,
            [new FigureScientificPoint(20, 130), new FigureScientificPoint(100, 130)],
            "N",
            "#FFFFFFFF",
            "#FFFFFFFF",
            0,
            "#FFFFFFFF",
            "Arial",
            7,
            1.25,
            true,
            true,
            2),
        new(
            Guid.NewGuid(),
            FigureScientificObjectKind.Colorbar,
            [new FigureScientificPoint(180, 20), new FigureScientificPoint(210, 120)],
            "Intensity",
            "#FFFFFFFF",
            "#FFFFFFFF",
            0,
            "#FFFFFFFF",
            "Arial",
            7,
            1.25,
            false,
            true,
            3,
            Minimum: 0,
            Maximum: 4095,
            Unit: "a.u.",
            Colormap: "magma"),
        new(
            Guid.NewGuid(),
            FigureScientificObjectKind.ChannelLegend,
            [new FigureScientificPoint(110, 90), new FigureScientificPoint(170, 140)],
            "Channels",
            "#FFFFFFFF",
            "#FF000000",
            80,
            "#FFFFFFFF",
            "Arial",
            7,
            1.25,
            false,
            true,
            4,
            ChannelLegendEntries:
            [
                new FigureChannelLegendEntry("DAPI", "#FF4FC3F7"),
                new FigureChannelLegendEntry("GFP", "#FF66BB6A"),
            ]),
    ];
}
