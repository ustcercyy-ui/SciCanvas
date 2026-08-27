using SciCanvas.Core.Export;

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
            FigureScientificObjectKind.Roi,
            [new FigureScientificPoint(100, 20), new FigureScientificPoint(150, 20), new FigureScientificPoint(150, 70), new FigureScientificPoint(100, 70)],
            "ROI 1",
            "#FF00E5FF",
            "#FF00E5FF",
            10,
            "#FFFFFFFF",
            "Arial",
            7,
            1.25,
            true,
            true,
            1),
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