using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using SpatialLinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;
using SpatialMapping = SciCanvas.Core.Linking.SpatialMapping;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class ExportProvenanceIntegrationTests
{
    [Fact]
    public void Create_RecordsCompositeRegistrationRoiScientificObjectsAndFontPolicy()
    {
        SourceAsset reference = CreateSource("HAADF.tif", 16);
        SourceAsset titanium = CreateSource("Ti.tif", 16);
        Guid groupId = Guid.NewGuid();
        Guid haadfChannel = Guid.NewGuid();
        Guid tiChannel = Guid.NewGuid();
        var firstLayer = CreateLayer(groupId, reference, haadfChannel, "HAADF", "#FFFFFFFF", 3);
        var secondLayer = CreateLayer(groupId, titanium, tiChannel, "Ti", "#FFFF0000", 4);
        var panel = new FigurePanelExportItem(
            reference,
            new PixelRect64(0, 0, 4, 4),
            new PixelRect64(0, 0, 100, 100),
            "a",
            true,
            PanelId: Guid.NewGuid(),
            ChannelLayers: [firstLayer, secondLayer]);
        var colorbar = new FigureScientificObjectExportItem(
            Guid.NewGuid(),
            FigureScientificObjectKind.Colorbar,
            [new FigureScientificPoint(110, 10), new FigureScientificPoint(130, 90)],
            "Ti intensity",
            "#FFFFFFFF",
            "#FF000000",
            0,
            "#FFFFFFFF",
            "Arial",
            7,
            1,
            false,
            true,
            1,
            10,
            90,
            "counts",
            "magma",
            ChannelId: tiChannel);
        var legend = new FigureScientificObjectExportItem(
            Guid.NewGuid(),
            FigureScientificObjectKind.ChannelLegend,
            [new FigureScientificPoint(140, 10), new FigureScientificPoint(195, 55)],
            "Channels",
            "#FFFFFFFF",
            "#AA000000",
            65,
            "#FFFFFFFF",
            "Arial",
            7,
            1,
            false,
            true,
            2,
            ChannelLegendEntries: [new FigureChannelLegendEntry("Ti", "#FFFF0000")]);
        var export = new FigureExportDocument(
            200,
            100,
            300,
            [panel],
            scientificObjects: [colorbar, legend],
            pdfFontStrategy: PdfFontStrategy.PreferEmbeddedWithOutlineFallback);

        SpatialMapping mapping = SpatialMapping.CreateIdentity(
            reference.Id, titanium.Id, 3, 4, DateTimeOffset.UnixEpoch.AddDays(1));
        var link = new SpatialLinkGroup(
            Guid.NewGuid(),
            "EDS linked view",
            reference.Id,
            [reference.Id, titanium.Id],
            SpatialLinkSyncOptions.Crop | SpatialLinkSyncOptions.Roi,
            [mapping]).EnsureValid();
        Guid referenceRoiId = Guid.NewGuid();
        Guid targetRoiId = Guid.NewGuid();
        var targetRoi = new RoiObject
        {
            Id = targetRoiId,
            AssetId = titanium.Id,
            SourceRevision = 4,
            GeometryKind = RoiGeometryKind.Polygon,
            SourceGeometry =
            [
                new MeasurementPoint(0, 0),
                new MeasurementPoint(3, 0),
                new MeasurementPoint(3, 3),
            ],
            Propagation = new RoiPropagationProvenance(referenceRoiId, targetRoiId, link.Id, mapping.Id),
        }.EnsureValid();
        var substitution = new FontSubstitutionRule("Helvetica Neue", "Arial");
        var resolved = new ResolvedFont(
            "Helvetica Neue", "Arial", FontResolutionKind.ExplicitSubstitution, substitution);

        FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
            export,
            "figure.pdf",
            "2.4.0-alpha",
            [reference, titanium],
            new FigurePreflightResult([]),
            sourceRevisions: new Dictionary<Guid, long> { [reference.Id] = 3, [titanium.Id] = 4 },
            fontResolutions: [resolved],
            linkGroups: [link],
            rois: [targetRoi]);

        Assert.Equal(groupId, Assert.Single(provenance.Panels).CompositeGroupId);
        Assert.Equal(2, provenance.Channels!.Count);
        FigureProvenanceChannel ti = Assert.Single(provenance.Channels, item => item.ChannelId == tiChannel);
        Assert.Equal(4, ti.SourceRevision);
        Assert.Equal(16, ti.BitDepth);
        Assert.Equal(65535, ti.DisplayMaximum);
        Assert.Equal(mapping.Id, Assert.Single(provenance.Registrations!).MappingId);
        Assert.Equal(targetRoiId, Assert.Single(provenance.RoiPropagations!).TargetRoiId);
        Assert.Equal(tiChannel, Assert.Single(provenance.Colorbars!).ChannelId);
        Assert.Equal("Ti", Assert.Single(Assert.Single(provenance.ChannelLegends!).Entries).Label);
        FigureProvenanceFontResolution font = Assert.Single(provenance.FontResolutions!);
        Assert.Equal("Helvetica Neue", font.RequestedFont);
        Assert.Equal("Arial", font.EffectiveFont);
        Assert.Equal("ExplicitSubstitution", font.ResolutionKind);
        FigureProvenancePdfFont pdf = Assert.Single(provenance.PdfFonts!);
        Assert.False(pdf.Embedded);
        Assert.True(pdf.Outlined);
        Assert.NotNull(pdf.FallbackReason);
    }

    private static FigureChannelLayerExportItem CreateLayer(
        Guid groupId,
        SourceAsset source,
        Guid channelId,
        string name,
        string color,
        long revision)
    {
        var descriptor = new ScientificChannelDescriptor(
            channelId,
            0,
            name,
            ScientificChannelSourceKind.ExternalAsset,
            ScientificSampleType.UInt16,
            16,
            DefaultColor: color);
        return new FigureChannelLayerExportItem(
            groupId,
            source,
            revision,
            new PixelRect64(0, 0, 4, 4),
            0,
            descriptor,
            new ChannelDisplaySettings(channelId, true, color, 1, 0, 65535, 1, false));
    }

    private static SourceAsset CreateSource(string name, int bitsPerChannel) => new(
        Guid.NewGuid(),
        name,
        name,
        new SourceFingerprint(32, DateTimeOffset.UnixEpoch, new string('A', 64), null),
        new ImageMetadata(new PixelSize64(4, 4), 1, bitsPerChannel, $"Gray{bitsPerChannel}"),
        SourceLinkState.Verified);
}
