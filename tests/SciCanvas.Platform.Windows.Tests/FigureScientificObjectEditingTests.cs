using System.Windows.Media;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class FigureScientificObjectEditingTests
{
    [Fact]
    public void DirectionMarker_CanMoveResizeAndUseExactAngle()
    {
        var scientificObject = new FigureScientificObjectViewModel(
            FigureScientificObjectKind.DirectionMarker,
            canvasWidth: 1000,
            canvasHeight: 800,
            dpi: 300,
            zIndex: 0)
        {
            PointsText = "100,100;300,100",
        };

        scientificObject.MoveBy(50, 25);

        Assert.Equal(150, scientificObject.Bounds.Left, 8);
        Assert.Equal(125, scientificObject.Bounds.Top, 8);

        scientificObject.DirectionAngleDegrees = 90;

        Assert.Equal(90, scientificObject.DirectionAngleDegrees, 8);
        Assert.Equal(0, scientificObject.Bounds.Width, 8);
        Assert.Equal(200, scientificObject.Bounds.Height, 8);

        scientificObject.SetResizePoint(420, 500);

        Assert.True(scientificObject.IsValid);
        Assert.NotEqual(90, scientificObject.DirectionAngleDegrees);
    }

    [Fact]
    public void ScientificObjectColorsAndColorbarOfferExpandedChoices()
    {
        var colorbar = new FigureScientificObjectViewModel(
            FigureScientificObjectKind.Colorbar,
            canvasWidth: 1000,
            canvasHeight: 800,
            dpi: 300,
            zIndex: 0)
        {
            StrokeColor = "#FF123456",
            FillColor = "#FF654321",
            TextColor = "#FFABCDEF",
            Colormap = "turbo",
        };

        Assert.Contains("turbo", colorbar.ColormapChoices);
        Assert.Contains("cividis", colorbar.ColormapChoices);
        Assert.IsType<LinearGradientBrush>(colorbar.ColorbarBrush);
        Assert.True(colorbar.IsValid);
    }

    [Fact]
    public void ColorbarAdapter_LinkedTracksChannelDisplaySettingsAndDetachedStopsTracking()
    {
        Guid channelId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        ChannelGroupMember initial = CreateChannel(
            channelId,
            assetId,
            minimum: 10,
            maximum: 110,
            colormap: "magma");
        var colorbar = new FigureScientificObjectViewModel(
            FigureScientificObjectKind.Colorbar,
            canvasWidth: 1000,
            canvasHeight: 800,
            dpi: 300,
            zIndex: 0);

        colorbar.SetAvailableChannels([initial]);
        colorbar.ChannelId = channelId;

        Assert.Equal(ColorbarBindingState.Linked, colorbar.ColorbarBindingState);
        Assert.Equal(10, colorbar.Minimum);
        Assert.Equal(110, colorbar.Maximum);
        Assert.Equal("magma", colorbar.Colormap);
        Assert.Equal(5, colorbar.ColorbarTicksAscending.Count);

        ChannelGroupMember updated = CreateChannel(
            channelId,
            assetId,
            minimum: 20,
            maximum: 220,
            colormap: "turbo");
        colorbar.SetAvailableChannels([updated]);

        Assert.Equal(20, colorbar.Minimum);
        Assert.Equal(220, colorbar.Maximum);
        Assert.Equal("turbo", colorbar.Colormap);

        colorbar.ColorbarBindingState = ColorbarBindingState.Detached;
        colorbar.Minimum = 25;
        colorbar.SetAvailableChannels([
            CreateChannel(channelId, assetId, minimum: 30, maximum: 330, colormap: "cividis"),
        ]);

        Assert.Equal(25, colorbar.Minimum);
        Assert.Equal(220, colorbar.Maximum);
        Assert.Equal("turbo", colorbar.Colormap);
        Assert.Equal(ColorbarBindingState.Detached, colorbar.CreateExportItem().EffectiveColorbar!.BindingState);
    }

    [Fact]
    public void ChannelLegendAdapter_RoundTripsCanonicalTypographyContainerAndChannelIds()
    {
        Guid channelId = Guid.NewGuid();
        var legend = new FigureScientificObjectViewModel(
            FigureScientificObjectKind.ChannelLegend,
            canvasWidth: 1000,
            canvasHeight: 800,
            dpi: 300,
            zIndex: 0)
        {
            ChannelEntriesText = $"{channelId}|DAPI|#FF4FC3F7",
            FontFamily = "Arial",
            FontSizePt = 9,
            IsBold = false,
            TextColor = "#FF102030",
            FillColor = "#CC203040",
            FillOpacityPercent = 60,
            StrokeColor = "#FF506070",
            StrokeWidthPt = 2,
            ChannelLegendPadding = 11,
        };

        FigureChannelLegendExportSpec export = legend.CreateExportItem().EffectiveChannelLegend!;
        ChannelLegendObject model = legend.CreateChannelLegendModel()!;

        Assert.Equal(channelId, Assert.Single(export.Items).ChannelId);
        Assert.Equal("DAPI", Assert.Single(model.Items).Label);
        Assert.Equal("Arial", export.FontFamily);
        Assert.Equal(9, export.FontSizePt);
        Assert.False(export.IsBold);
        Assert.Equal("#FF102030", export.TextColor);
        Assert.Equal("#CC203040", export.BackgroundColor);
        Assert.Equal(60, export.BackgroundOpacityPercent);
        Assert.Equal("#FF506070", export.BorderColor);
        Assert.Equal(2, export.BorderWidthPt);
        Assert.Equal(11, export.PaddingPixels);
    }

    [Fact]
    public void PolygonAnnotation_VertexEditingUsesWholeCandidateAndKeepsFullBounds()
    {
        var polygon = new FigureScientificObjectViewModel(
            FigureScientificObjectKind.PolygonAnnotation,
            canvasWidth: 1000,
            canvasHeight: 800,
            dpi: 300,
            zIndex: 0);
        Assert.True(polygon.TrySetPolygonPoints(
        [
            new FigureScientificPoint(100, 100),
            new FigureScientificPoint(200, 100),
            new FigureScientificPoint(900, 700),
            new FigureScientificPoint(100, 700),
        ]));

        Assert.Equal(100, polygon.Bounds.Left, 8);
        Assert.Equal(100, polygon.Bounds.Top, 8);
        Assert.Equal(900, polygon.Bounds.Right, 8);
        Assert.Equal(700, polygon.Bounds.Bottom, 8);

        string beforeRejectedMove = polygon.PointsText;
        Assert.False(polygon.TryMovePolygonVertex(2, 1001, 700));
        Assert.Equal(beforeRejectedMove, polygon.PointsText);

        Assert.True(polygon.TryMovePolygonVertex(2, 850, 650));
        Assert.True(polygon.TryInsertPolygonVertex(150, 100, out int insertedIndex));
        Assert.Equal(5, polygon.CreateExportItem().Points.Count);
        Assert.True(polygon.TryDeletePolygonVertex(insertedIndex));
        Assert.Equal(4, polygon.CreateExportItem().Points.Count);
        Assert.True(polygon.TryDeletePolygonVertex(1));
        Assert.Equal(3, polygon.CreateExportItem().Points.Count);

        string minimumPolygon = polygon.PointsText;
        Assert.False(polygon.TryDeletePolygonVertex(0));
        Assert.Equal(minimumPolygon, polygon.PointsText);
        Assert.False(polygon.TrySetPolygonPoints(
        [
            new FigureScientificPoint(10, 10),
            new FigureScientificPoint(20, 20),
            new FigureScientificPoint(30, 30),
        ]));
        Assert.Equal(minimumPolygon, polygon.PointsText);
    }

    [Fact]
    public void PolygonAnnotationDraft_CompletesOnlyValidGeometryAndCancelsWithoutMutation()
    {
        var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);

        figure.BeginPolygonAnnotationCommand.Execute(null);
        Assert.True(figure.HasPendingPolygonAnnotation);
        Assert.True(figure.TryAddPolygonAnnotationDraftVertex(100, 100));
        Assert.True(figure.TryAddPolygonAnnotationDraftVertex(300, 100));
        Assert.False(figure.CompletePendingPolygonAnnotation());
        Assert.Empty(figure.ScientificObjects);

        Assert.True(figure.TryAddPolygonAnnotationDraftVertex(260, 280));
        Assert.True(figure.CompletePendingPolygonAnnotation());
        FigureScientificObjectViewModel polygon = Assert.Single(figure.ScientificObjects);
        Assert.Equal(FigureScientificObjectKind.PolygonAnnotation, polygon.Kind);
        Assert.Equal("Polygon Annotation", polygon.KindDisplayName);
        Assert.Equal(3, polygon.CreateExportItem().Points.Count);
        Assert.False(figure.HasPendingPolygonAnnotation);

        figure.BeginPolygonAnnotationCommand.Execute(null);
        Assert.True(figure.TryAddPolygonAnnotationDraftVertex(400, 400));
        Assert.True(figure.CancelPendingPolygonAnnotation());
        Assert.False(figure.HasPendingPolygonAnnotation);
        Assert.Single(figure.ScientificObjects);
    }

    [Fact]
    public void AssistedRegionCandidatesUseIndependentEditableColors()
    {
        var first = new AssistedRegionCandidateViewModel(
            CreateCandidate(1),
            calibration: null,
            AssistedRegionMode.BrightParticles);
        var second = new AssistedRegionCandidateViewModel(
            CreateCandidate(2),
            calibration: null,
            AssistedRegionMode.BrightParticles);

        Assert.NotEqual(first.Color, second.Color);
        first.Color = "#FF336699";
        Assert.Equal("#FF336699", first.OverlayStroke);
        Assert.StartsWith("#24", first.OverlayFill, StringComparison.Ordinal);

        first.MarkCommitted();

        Assert.StartsWith("#38", first.OverlayFill, StringComparison.Ordinal);
    }

    private static AssistedRegionCandidate CreateCandidate(int id) => new(
        id,
        new PixelRect64(id * 10, id * 10, 20, 20),
        id * 10 + 10,
        id * 10 + 10,
        300,
        80,
        0.85,
        1.2);

    private static ChannelGroupMember CreateChannel(
        Guid channelId,
        Guid assetId,
        double minimum,
        double maximum,
        string colormap)
    {
        var display = new ChannelDisplaySettings(
            channelId,
            true,
            "#FF4FC3F7",
            1,
            minimum,
            maximum,
            1,
            false,
            colormap);
        return new ChannelGroupMember(
            channelId,
            assetId,
            ChannelPlaneSelector.ExternalAsset(0),
            "DAPI",
            "nucleus",
            "#FF4FC3F7",
            ChannelNameOrigin.User,
            true,
            display)
        {
            SourceRevision = 1,
        }.EnsureValid();
    }
}
