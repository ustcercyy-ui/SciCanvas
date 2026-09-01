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
        SourceAsset titanium = CreateSource("Ti.tif", 16, channels: 3);
        Guid groupId = Guid.NewGuid();
        Guid haadfChannel = Guid.NewGuid();
        Guid tiChannel = Guid.NewGuid();
        SpatialMapping mapping = SpatialMapping.CreateIdentity(
            reference.Id, titanium.Id, 3, 4, DateTimeOffset.UnixEpoch.AddDays(1));
        var resampling = new RegisteredPlaneResamplingSpec(
            mapping,
            new RegisteredReferenceGrid(
                new ScientificPlaneRef(reference.Id, 3, ChannelPlaneSelector.ExternalAsset(0)),
                new PixelRect64(0, 0, 4, 4)),
            titanium.Metadata.PixelSize,
            RegisteredInterpolation.Bilinear,
            RegisteredBorderPolicy.Transparent);
        var firstLayer = CreateLayer(groupId, reference, haadfChannel, "HAADF", "#FFFFFFFF", 3);
        var secondLayer = CreateLayer(
            groupId,
            titanium,
            tiChannel,
            "Ti",
            "#FFFF0000",
            4,
            ChannelPlaneSelector.InterleavedComponent(0, 2),
            resampling);
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
        Assert.Equal("InterleavedComponent", ti.SourceKind);
        Assert.Equal(2, ti.ComponentIndex);
        Assert.Equal(mapping.Id, ti.MappingId);
        Assert.Equal("Identity", ti.MappingKind);
        Assert.Equal(mapping.Matrix, ti.MappingMatrix);
        Assert.Equal("Bilinear", ti.Interpolation);
        Assert.Equal("Transparent", ti.BorderPolicy);
        Assert.Equal("ContinuousDisplay", ti.PlaneSemantic);
        Assert.Equal(3, ti.MappingSourceRevision);
        Assert.Equal(4, ti.MappingTargetRevision);
        FigureProvenanceReferenceGrid referenceGrid = Assert.IsType<FigureProvenanceReferenceGrid>(
            ti.ReferenceGrid);
        Assert.Equal(reference.Id, referenceGrid.AssetId);
        Assert.Equal(new PixelRect64(0, 0, 4, 4), referenceGrid.Region);
        Assert.Equal(4, referenceGrid.Width);
        Assert.Equal(4, referenceGrid.Height);
        Assert.Equal("ExternalAsset", referenceGrid.SourceKind);
        Assert.Equal(mapping.Id, Assert.Single(provenance.Registrations!).MappingId);
        FigureProvenanceRoiPropagation roiPropagation = Assert.Single(provenance.RoiPropagations!);
        Assert.Equal(targetRoiId, roiPropagation.TargetRoiId);
        Assert.Equal(1, roiPropagation.TargetCoverageFraction);
        FigureProvenanceColorbar colorbarProvenance = Assert.Single(provenance.Colorbars!);
        Assert.Equal(tiChannel, colorbarProvenance.ChannelId);
        Assert.Equal("Linked", colorbarProvenance.BindingState);
        Assert.Equal("Vertical", colorbarProvenance.Orientation);
        Assert.Equal(5, colorbarProvenance.Ticks.Count);
        FigureProvenanceChannelLegend legendProvenance = Assert.Single(provenance.ChannelLegends!);
        Assert.Equal("Ti", Assert.Single(legendProvenance.Entries).Label);
        Assert.Equal("Arial", legendProvenance.FontFamily);
        Assert.Equal(7, legendProvenance.FontSizePt);
        Assert.Equal("#FFFFFFFF", legendProvenance.TextColor);
        Assert.Equal("#AA000000", legendProvenance.BackgroundColor);
        Assert.Equal(65, legendProvenance.BackgroundOpacityPercent);
        Assert.Equal("#FFFFFFFF", legendProvenance.BorderColor);
        Assert.Equal(1, legendProvenance.BorderWidthPt);
        Assert.Equal(5, legendProvenance.PaddingPixels);
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
        long revision,
        ChannelPlaneSelector? planeSelector = null,
        RegisteredPlaneResamplingSpec? resampling = null)
    {
        planeSelector ??= ChannelPlaneSelector.ExternalAsset(0);
        ScientificChannelDescriptor descriptor = planeSelector.CreateChannelDescriptor(
            channelId,
            name,
            ScientificSampleType.UInt16,
            16,
            role: null,
            defaultColor: color);
        return new FigureChannelLayerExportItem(
            groupId,
            source,
            revision,
            new PixelRect64(0, 0, 4, 4),
            0,
            descriptor,
            new ChannelDisplaySettings(channelId, true, color, 1, 0, 65535, 1, false),
            RegistrationResampling: resampling);
    }

    private static SourceAsset CreateSource(
        string name,
        int bitsPerChannel,
        int channels = 1) => new(
        Guid.NewGuid(),
        name,
        name,
        new SourceFingerprint(32, DateTimeOffset.UnixEpoch, new string('A', 64), null),
        new ImageMetadata(
            new PixelSize64(4, 4),
            channels,
            bitsPerChannel,
            channels == 1 ? $"Gray{bitsPerChannel}" : $"Rgb{bitsPerChannel * 3}"),
        SourceLinkState.Verified);
}
