using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class WorkspaceDomainTests
{
    [Fact]
    public void MeasurementValue_RemainsInvariantWhenPanelFrameChanges()
    {
        Guid assetId = Guid.NewGuid();
        var calibration = new SpatialCalibration(
            assetId,
            10,
            10,
            "nm",
            CalibrationOrigin.Manual);
        var measurement = new ScientificMeasurement(
            Guid.NewGuid(),
            assetId,
            ScientificMeasurementKind.Length,
            new MeasurementPoint(0, 0),
            new MeasurementPoint(100, 0));
        FigurePanel panel = CreatePanel(assetId, new FigureRectMm(0, 0, 50, 40));

        double before = Assert.IsType<double>(measurement.PhysicalValue(calibration));
        FigurePanel resized = panel.ResizeFrame(100, 80);
        double after = Assert.IsType<double>(measurement.PhysicalValue(calibration));

        Assert.Equal(1000, before, 9);
        Assert.Equal(before, after, 9);
        Assert.Equal(100, resized.Frame.Width, 9);
    }

    [Fact]
    public void ReplaceAsset_PreservesFrameLabelStyleAndOrder()
    {
        ScientificAsset previous = CreateAsset(calibrated: true);
        ScientificAsset replacement = CreateAsset(calibrated: true);
        StyleOverride localStyle = new(PanelLabel: new TextStyle("Arial", 9, true, "#FFFFFFFF"));
        FigurePanel panel = CreatePanel(previous.Id, new FigureRectMm(12, 18, 52, 41)) with
        {
            Label = "(c)",
            ZIndex = 4,
            StyleOverride = localStyle,
        };

        PanelReplacementResult result = PanelReplacementService.Replace(
            panel,
            previous,
            replacement,
            []);

        Assert.Equal(replacement.Id, result.Panel.AssetId);
        Assert.Equal(panel.Frame, result.Panel.Frame);
        Assert.Equal(panel.Label, result.Panel.Label);
        Assert.Equal(panel.ZIndex, result.Panel.ZIndex);
        Assert.Same(localStyle, result.Panel.StyleOverride);
    }

    [Fact]
    public void ReplaceAssetWithoutCalibration_InvalidatesScaleBarAndMeasurements()
    {
        ScientificAsset previous = CreateAsset(calibrated: true);
        ScientificAsset replacement = CreateAsset(calibrated: false);
        FigurePanel panel = CreatePanel(previous.Id, new FigureRectMm(0, 0, 50, 40));
        var scaleBar = new ScaleBarObject
        {
            Id = Guid.NewGuid(),
            AssetId = previous.Id,
            PanelId = panel.Id,
            SourceRevision = previous.Source.SourceRevision,
            PhysicalLength = 500,
            Unit = "nm",
            Placement = new ScaleBarPlacement(ScaleBarAnchor.BottomRight, 2, 2),
        };
        var measurement = new MeasurementObject
        {
            Id = Guid.NewGuid(),
            AssetId = previous.Id,
            PanelId = panel.Id,
            SourceRevision = previous.Source.SourceRevision,
            Measurement = new ScientificMeasurement(
                Guid.NewGuid(),
                previous.Id,
                ScientificMeasurementKind.Length,
                new MeasurementPoint(0, 0),
                new MeasurementPoint(100, 0)),
        };

        PanelReplacementResult result = PanelReplacementService.Replace(
            panel,
            previous,
            replacement,
            [scaleBar, measurement]);

        ScaleBarObject replacedScaleBar = Assert.IsType<ScaleBarObject>(result.ScientificObjects[0]);
        MeasurementObject replacedMeasurement = Assert.IsType<MeasurementObject>(result.ScientificObjects[1]);
        Assert.Equal(ScientificValidityState.Invalid, replacedScaleBar.Validity.State);
        Assert.Equal(ScientificValidityState.ReviewRequired, replacedMeasurement.Validity.State);
        Assert.Equal(previous.Id, replacedMeasurement.AssetId);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void EffectiveDpi_UsesVisibleSourcePixelsAndPhysicalPanelSize()
    {
        double full = EffectiveDpiCalculator.Calculate(
            4000,
            2000,
            NormalizedRect.Full,
            100,
            50);
        double cropped = EffectiveDpiCalculator.Calculate(
            4000,
            2000,
            new NormalizedRect(0.25, 0, 0.5, 1),
            100,
            50);
        double widerPanel = EffectiveDpiCalculator.Calculate(
            4000,
            2000,
            NormalizedRect.Full,
            200,
            50);

        Assert.Equal(1016, full, 6);
        Assert.Equal(508, cropped, 6);
        Assert.Equal(508, widerPanel, 6);
    }

    [Fact]
    public void NormalizedRect_SourcePixelFactoryRoundTripsRandomHalfOpenRectanglesExactly()
    {
        var random = new Random(0x5C1CA);
        (long Width, long Height)[] sourceSizes =
        [
            (101, 103),
            (1009, 1013),
            (8191, 8209),
            (1_000_003, 1_000_033),
        ];

        foreach ((long sourceWidth, long sourceHeight) in sourceSizes)
        {
            for (int iteration = 0; iteration < 2_000; iteration++)
            {
                long x = random.NextInt64(sourceWidth);
                long y = random.NextInt64(sourceHeight);
                long width = random.NextInt64(1, sourceWidth - x + 1);
                long height = random.NextInt64(1, sourceHeight - y + 1);
                var expected = new PixelRect64(x, y, width, height);

                PixelRect64 actual = NormalizedRect
                    .FromSourcePixels(expected, sourceWidth, sourceHeight)
                    .ToSourcePixels(sourceWidth, sourceHeight);

                Assert.Equal(expected, actual);
            }
        }
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(100, 102, 1, 1)]
    [InlineData(94, 96, 7, 7)]
    public void NormalizedRect_PreservesPrimeSizedSourceEdges(
        long x,
        long y,
        long width,
        long height)
    {
        var expected = new PixelRect64(x, y, width, height);

        PixelRect64 actual = NormalizedRect
            .FromSourcePixels(expected, 101, 103)
            .ToSourcePixels(101, 103);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizedRect_PreservesPixelsBeyondDoubleIntegerPrecision()
    {
        const long sourceWidth = 9_007_199_254_741_031;
        const long sourceHeight = 9_007_199_254_741_111;
        var expected = new PixelRect64(
            sourceWidth - 97,
            sourceHeight - 89,
            97,
            89);

        PixelRect64 actual = NormalizedRect
            .FromSourcePixels(expected, sourceWidth, sourceHeight)
            .ToSourcePixels(sourceWidth, sourceHeight);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizedRect_EqualityIgnoresDerivedCanonicalPixelCache()
    {
        var expected = new NormalizedRect(0, 0, 1, 1);
        NormalizedRect derived = NormalizedRect.FromSourcePixels(
            new PixelRect64(0, 0, 101, 103),
            101,
            103);

        Assert.Equal(expected, derived);
    }

    [Fact]
    public void FigurePanel_ManualCropKeepsCanonicalHalfOpenPixelRectangle()
    {
        Guid sourceId = Guid.NewGuid();
        var expected = new PixelRect64(59, 6, 15, 48);
        FigurePanel panel = CreatePanel(
                sourceId,
                new FigureRectMm(0, 0, 40, 30))
            .WithManualCrop(expected, 101, 103);

        Assert.Equal(expected, panel.ManualCropPixels);
        Assert.Equal(expected, panel.ResolveSourcePixels(101, 103));
        Assert.Equal(
            expected,
            panel.Crop.ToSourcePixels(101, 103));
    }

    [Fact]
    public void ProjectStyleChange_UpdatesInheritedLabelsButPreservesLocalOverride()
    {
        ProjectStyle initial = ProjectStyle.Default with
        {
            PanelLabel = ProjectStyle.Default.PanelLabel with { FontSizePt = 8 },
        };
        ProjectStyle updated = initial with
        {
            PanelLabel = initial.PanelLabel with { FontSizePt = 9 },
        };
        StyleOverride local = new(
            PanelLabel: initial.PanelLabel with { FontSizePt = 10 });

        ResolvedProjectStyle inherited = ProjectStyleResolver.Resolve(updated);
        ResolvedProjectStyle overridden = ProjectStyleResolver.Resolve(updated, panel: local);

        Assert.Equal(9, inherited.PanelLabel.Value.FontSizePt);
        Assert.Equal(StyleInheritanceSource.Project, inherited.PanelLabel.Source);
        Assert.Equal(10, overridden.PanelLabel.Value.FontSizePt);
        Assert.Equal(StyleInheritanceSource.Panel, overridden.PanelLabel.Source);
    }

    [Fact]
    public void MatchWidth_CapturesAllPanelsInOneReversibleMutation()
    {
        Guid assetId = Guid.NewGuid();
        Guid figureId = Guid.NewGuid();
        FigurePanel[] panels = Enumerable.Range(0, 8)
            .Select(index => CreatePanel(
                assetId,
                new FigureRectMm(index * 12, 0, 10 + index, 8),
                figureId))
            .ToArray();
        FigurePanel[] original = panels.Select(panel => panel).ToArray();

        LayoutMutation transaction = FigureLayoutService.MatchSize(
            panels,
            panels[0].Id,
            MatchSizeMode.Width);
        FigurePanel[] afterUndo = transaction.Before.ToArray();

        Assert.Equal(8, transaction.After.Count);
        Assert.All(transaction.After, panel => Assert.Equal(panels[0].Frame.Width, panel.Frame.Width));
        Assert.Equal(original, afterUndo);
    }

    [Fact]
    public void QcEngine_DetectsMissingSourceCalibrationLabelAndLowDpi()
    {
        ScientificAsset asset = CreateAsset(calibrated: false) with
        {
            LinkState = SourceLinkState.Missing,
        };
        Guid figureId = Guid.NewGuid();
        FigurePanel panel = CreatePanel(asset.Id, new FigureRectMm(0, 0, 178, 120), figureId) with
        {
            Label = string.Empty,
        };
        var figure = new ScientificFigure(
            figureId,
            "Figure 1",
            178,
            120,
            [panel],
            [],
            null,
            DateTimeOffset.UtcNow);
        ScientificProject project = CreateProject(asset, figure);

        IReadOnlyList<QcResult> issues = new QcEngine().Evaluate(
            new QcContext(project, new QcConfiguration(MinimumEffectiveDpi: 600)));

        Assert.Contains(issues, issue => issue.RuleId == "source.tracking");
        Assert.Contains(issues, issue => issue.RuleId == "calibration.asset");
        Assert.Contains(issues, issue => issue.RuleId == "panel-label.sequence");
        Assert.Contains(issues, issue => issue.RuleId == "resolution.effective-dpi");
    }

    [Fact]
    public void PanelLabelScheme_GeneratesBeyondZForBothAlphabeticCases()
    {
        Assert.Equal("z", PanelLabelGenerator.Generate(25, PanelLabelScheme.LowerAlpha));
        Assert.Equal("aa", PanelLabelGenerator.Generate(26, PanelLabelScheme.LowerAlpha));
        Assert.Equal("Z", PanelLabelGenerator.Generate(25, PanelLabelScheme.UpperAlpha));
        Assert.Equal("AA", PanelLabelGenerator.Generate(26, PanelLabelScheme.UpperAlpha));
    }

    [Theory]
    [InlineData("Numeric", "1", "2")]
    [InlineData("Custom", "SEM", "EDS")]
    [InlineData("None", "", "")]
    public void QcEngine_UsesFigurePanelLabelSchemeInsteadOfHardcodedLowerAlpha(
        string schemeName,
        string firstLabel,
        string secondLabel)
    {
        ScientificAsset asset = CreateAsset(calibrated: true);
        Guid figureId = Guid.NewGuid();
        FigurePanel first = CreatePanel(asset.Id, new FigureRectMm(0, 0, 40, 40), figureId) with
        {
            Label = firstLabel,
        };
        FigurePanel second = CreatePanel(asset.Id, new FigureRectMm(50, 0, 40, 40), figureId) with
        {
            Label = secondLabel,
        };
        var figure = new ScientificFigure(
            figureId,
            "Figure 1",
            100,
            50,
            [first, second],
            [],
            null,
            DateTimeOffset.UtcNow)
        {
            LabelScheme = Enum.Parse<PanelLabelScheme>(schemeName),
        };
        ScientificProject project = CreateProject(asset, figure);

        IReadOnlyList<QcResult> issues = new QcEngine().Evaluate(
            new QcContext(project, new QcConfiguration()));

        Assert.DoesNotContain(issues, issue => issue.RuleId == "panel-label.sequence");
    }

    private static ScientificProject CreateProject(
        ScientificAsset asset,
        ScientificFigure figure) => new(
            ScientificProject.CurrentSchemaVersion,
            Guid.NewGuid(),
            "Workspace test",
            new Dictionary<Guid, ScientificAsset> { [asset.Id] = asset },
            new Dictionary<Guid, ScientificFigure> { [figure.Id] = figure },
            ProjectStyle.Default,
            new Dictionary<Guid, ScientificObject>(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static FigurePanel CreatePanel(
        Guid assetId,
        FigureRectMm frame,
        Guid? figureId = null) => new(
            Guid.NewGuid(),
            figureId ?? Guid.NewGuid(),
            assetId,
            frame,
            NormalizedRect.Full,
            PanelFitMode.Manual,
            0,
            new PanelAdjustments(),
            null,
            [],
            "(a)",
            0);

    private static ScientificAsset CreateAsset(bool calibrated)
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var fingerprint = new SourceFingerprint(
            1024,
            now,
            new string('A', 64),
            null);
        var metadata = new ImageMetadata(
            new PixelSize64(2048, 1536),
            1,
            16,
            "Gray16");
        SpatialCalibration? calibration = calibrated
            ? new SpatialCalibration(id, 5, 5, "nm", CalibrationOrigin.Manual)
            : null;
        return new ScientificAsset(
            id,
            $"Asset-{id:N}",
            new AssetSourceReference(
                $"C:\\data\\{id:N}.tif",
                $"{id:N}.tif",
                fingerprint,
                1),
            metadata,
            AssetKind.Sem,
            calibration,
            new Dictionary<string, object?>(),
            [],
            null,
            SourceLinkState.Verified,
            now,
            now);
    }
}
