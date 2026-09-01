using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class FontUsageCollectorTests
{
    [Fact]
    public void Collect_TraversesGlobalPanelAnnotationOverlayScientificAndRoiFontsWithLocations()
    {
        Guid figureId = Guid.NewGuid();
        Guid panelId = Guid.NewGuid();
        Guid annotationId = Guid.NewGuid();
        Guid overlayId = Guid.NewGuid();
        Guid legendId = Guid.NewGuid();
        Guid projectionId = Guid.NewGuid();
        FigureExportDocument document = CreateDocument(
            panelId,
            annotationId,
            overlayId,
            legendId,
            projectionId);

        IReadOnlyList<FontUsage> usages = FontUsageCollector.Collect(document, figureId);

        Assert.Contains(usages, usage =>
            usage.RequestedFont == "GlobalFace" &&
            usage.UsageKind == FontUsageKind.FigureDefault &&
            usage.FigureId == figureId);
        Assert.Contains(usages, usage =>
            usage.RequestedFont == "LocalPanelFace" &&
            usage.UsageKind == FontUsageKind.PanelLabel &&
            usage.PanelId == panelId);
        Assert.Contains(usages, usage =>
            usage.RequestedFont == "LocalScaleFace" &&
            usage.UsageKind == FontUsageKind.ScaleBarText &&
            usage.PanelId == panelId);
        Assert.Contains(usages, usage =>
            usage.RequestedFont == "AnnotationFace" &&
            usage.UsageKind == FontUsageKind.Annotation &&
            usage.ObjectId == annotationId);
        Assert.Contains(usages, usage =>
            usage.RequestedFont == "OverlayFace" &&
            usage.UsageKind == FontUsageKind.MeasurementOverlayLabel &&
            usage.PanelId == panelId &&
            usage.ObjectId == overlayId);
        Assert.Contains(usages, usage =>
            usage.RequestedFont == "LegendFace" &&
            usage.UsageKind == FontUsageKind.ChannelLegend &&
            usage.ObjectId == legendId);
        Assert.Contains(usages, usage =>
            usage.RequestedFont == "RoiFace" &&
            usage.UsageKind == FontUsageKind.RoiLabel &&
            usage.PanelId == panelId &&
            usage.ObjectId == projectionId);
    }

    [Fact]
    public void Resolve_UsesCollectorForTypedLegendAndEveryRequestedFont()
    {
        Guid panelId = Guid.NewGuid();
        FigureExportDocument document = CreateDocument(
            panelId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        string[] requested = FontUsageCollector.Collect(document)
            .Select(usage => usage.RequestedFont)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        FontSubstitutionRule[] substitutions = requested
            .Where(font => !string.Equals(font, "Arial", StringComparison.OrdinalIgnoreCase))
            .Select(font => new FontSubstitutionRule(font, "Arial"))
            .ToArray();

        ResolvedFigureExportDocument resolved = FigureExportFontResolver.Resolve(
            document,
            substitutions,
            new FixedFontCatalog(["Arial"]));

        Assert.All(requested, font =>
            Assert.Contains(resolved.FontResolutions, resolution =>
                resolution.RequestedFamily == font));
        FigureScientificObjectExportItem legend = Assert.Single(
            resolved.Document.ScientificObjects,
            item => item.Kind == FigureScientificObjectKind.ChannelLegend);
        Assert.Equal("Arial", legend.FontFamily);
        Assert.Equal("Arial", legend.EffectiveChannelLegend!.FontFamily);
        Assert.Equal("Arial", Assert.Single(resolved.Document.MeasurementOverlays).Style.LabelFontFamily);
        Assert.Equal("Arial", Assert.Single(resolved.Document.Panels).StyleOverride!.PanelLabel!.FontFamily);
    }

    [Fact]
    public void Provenance_RecordsEveryFontReportedByCollector()
    {
        FigureExportDocument document = CreateDocument(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        string[] expected = FontUsageCollector.Collect(document)
            .Select(usage => usage.RequestedFont)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(font => font, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
            document,
            "figure.pdf",
            "test",
            document.Panels.Select(panel => panel.Source).DistinctBy(source => source.Id).ToArray(),
            new FigurePreflightResult([]));

        Assert.Equal(
            expected,
            provenance.FontResolutions!
                .Select(item => item.RequestedFont)
                .OrderBy(font => font, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            expected,
            provenance.PdfFonts!
                .Select(item => item.RequestedFont)
                .OrderBy(font => font, StringComparer.OrdinalIgnoreCase));
    }

    private static FigureExportDocument CreateDocument(
        Guid panelId,
        Guid annotationId,
        Guid overlayId,
        Guid legendId,
        Guid projectionId)
    {
        Guid sourceId = Guid.NewGuid();
        Guid measurementId = Guid.NewGuid();
        Guid roiId = Guid.NewGuid();
        var source = new SourceAsset(
            sourceId,
            "source.tif",
            "C:\\source.tif",
            new SourceFingerprint(10, DateTimeOffset.UnixEpoch, new string('A', 64), null),
            new ImageMetadata(new PixelSize64(100, 100), 1, 16, "Gray16"),
            SourceLinkState.Verified);
        var panel = new FigurePanelExportItem(
            source,
            new PixelRect64(0, 0, 100, 100),
            new PixelRect64(0, 0, 100, 100),
            "a",
            true,
            new FigureScaleBarExportSpec(1, 20, "px", true),
            StyleOverride: new StyleOverride(
                PanelLabel: new TextStyle("LocalPanelFace", 8, true, "#FF000000"),
                ScaleBarText: new TextStyle("LocalScaleFace", 7, false, "#FFFFFFFF")),
            PanelId: panelId);
        var annotation = new FigureAnnotationExportItem(
            "text",
            10,
            10,
            10,
            10,
            "note",
            "#FF000000",
            "#00000000",
            0,
            "#FF000000",
            "AnnotationFace",
            7,
            1,
            false,
            true,
            0)
        {
            Id = annotationId,
        };
        var overlay = new FigureMeasurementOverlayExportItem(new MeasurementOverlayObject
        {
            Id = overlayId,
            AssetId = sourceId,
            PanelId = panelId,
            SourceRevision = 1,
            MeasurementId = measurementId,
            SourceGeometry = new ScientificMeasurement(
                measurementId,
                sourceId,
                ScientificMeasurementKind.Length,
                new MeasurementPoint(10, 10),
                new MeasurementPoint(40, 40),
                SourceRevision: 1),
            Style = new FigureMeasurementOverlayStyle(
                "#FFFFFFFF", 1, "solid", "#00000000", 0,
                "#FFFFFFFF", "#FF000000", 6, true,
                "#FFFFFFFF", "OverlayFace", 7, false, true),
        });
        var legend = new FigureScientificObjectExportItem(
            legendId,
            FigureScientificObjectKind.ChannelLegend,
            [new FigureScientificPoint(50, 10), new FigureScientificPoint(90, 40)],
            "Channels",
            "#FFFFFFFF",
            "#FF000000",
            50,
            "#FFFFFFFF",
            "LegacyUnusedFace",
            7,
            1,
            false,
            true,
            1,
            ChannelLegendEntries: [new FigureChannelLegendEntry("DAPI", "#FFFFFFFF")],
            ChannelLegend: new FigureChannelLegendExportSpec(
                [new FigureChannelLegendEntry("DAPI", "#FFFFFFFF")],
                "LegendFace",
                7,
                false,
                "#FFFFFFFF",
                "#FF000000",
                50,
                "#FFFFFFFF",
                1,
                5));
        var roi = new RoiObject
        {
            Id = roiId,
            AssetId = sourceId,
            SourceRevision = 1,
            GeometryKind = RoiGeometryKind.Rectangle,
            SourceGeometry = [new MeasurementPoint(10, 10), new MeasurementPoint(30, 30)],
            Style = new RoiStyle(
                RoiStyle.Default.Shape,
                new TextStyle("RoiFace", 7, false, "#FFFFFFFF"),
                "ROI"),
        }.EnsureValid();
        var projection = new FigureRoiProjectionExportItem(
            new RoiFigureProjectionObject
            {
                Id = projectionId,
                RoiId = roiId,
                PanelId = panelId,
                AssetId = sourceId,
                SourceRevision = 1,
            },
            roi);
        var global = new FigureGlobalStyle(
            "GlobalFace",
            7,
            1,
            "#FF000000",
            "#FFFFFFFF",
            "#FFFFFFFF",
            PanelLabelFontFamily: "GlobalPanelFace",
            ScaleBarFontFamily: "GlobalScaleFace");
        return new FigureExportDocument(
            100,
            100,
            96,
            [panel],
            [annotation],
            globalStyle: global,
            measurementOverlays: [overlay],
            scientificObjects: [legend],
            roiProjections: [projection]);
    }
}
