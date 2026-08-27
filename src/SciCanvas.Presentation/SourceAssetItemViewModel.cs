using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;

namespace SciCanvas.Presentation;

public sealed class SourceAssetItemViewModel : ObservableObject
{
    private SourceAsset _asset;
    private BitmapSource _preview;
    private readonly Dictionary<int, BitmapSource> _framePreviews = [];
    private ScientificMeasurementViewModel? _selectedMeasurement;
    private long _sourceRevision = 1;
    private int _usageCount;

    public SourceAssetItemViewModel(SourceAsset asset, BitmapSource preview)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _framePreviews[0] = _preview;
        Calibration = new CalibrationEditorViewModel(
            asset.Id,
            asset.Metadata);
        Calibration.Changed += OnCalibrationChanged;
        Calibration.EditCompleted += OnScienceEditCompleted;
    }

    public event EventHandler? ScienceChanged;

    public event EventHandler? ScienceEditCompleted;

    public event EventHandler? MeasurementSelectionChanged;

    public event EventHandler? AnalysisChanged;

    public SourceAsset Asset => _asset;

    public long SourceRevision => _sourceRevision;

    public string SourceRevisionText => $"Revision {_sourceRevision}";

    public string LinkStateText => Asset.LinkState switch
    {
        SourceLinkState.Verified => "Verified",
        SourceLinkState.Relocated => "Relinked",
        SourceLinkState.Modified => "Changed",
        SourceLinkState.Missing => "Missing",
        _ => "Unverified",
    };

    public string AssetKindText
    {
        get
        {
            string value = DisplayName.ToLowerInvariant();
            if (value.Contains("sem")) return "SEM";
            if (value.Contains("stem")) return "STEM";
            if (value.Contains("tem")) return "TEM";
            if (value.Contains("ebsd")) return "EBSD";
            if (value.Contains("eds") || value.Contains("edx")) return "EDS";
            if (value.Contains("afm")) return "AFM";
            if (value.Contains("xrd")) return "XRD";
            if (value.Contains("graph") || value.Contains("plot")) return "Graph";
            if (value.Contains("schematic")) return "Schematic";
            return "Other";
        }
    }

    public int UsageCount => _usageCount;

    public string UsageText => _usageCount == 0 ? "未使用" : $"{_usageCount} 个 Panel";

    public BitmapSource Preview => _preview;

    public CalibrationEditorViewModel Calibration { get; }

    public ObservableCollection<ScientificMeasurementViewModel> Measurements { get; } = [];

    public ObservableCollection<ScientificImageAnalysisResult> AnalysisResults { get; } = [];

    public ScientificMeasurementViewModel? SelectedMeasurement
    {
        get => _selectedMeasurement;
        set
        {
            if (ReferenceEquals(_selectedMeasurement, value))
            {
                return;
            }

            if (_selectedMeasurement is not null)
            {
                _selectedMeasurement.IsSelected = false;
            }

            _selectedMeasurement = value;
            if (_selectedMeasurement is not null)
            {
                _selectedMeasurement.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMeasurementStatusText));
            MeasurementSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string DisplayName => Asset.DisplayName;

    public string OriginalPath => Asset.OriginalPath;

    public long Width => Asset.Metadata.PixelSize.Width;

    public long Height => Asset.Metadata.PixelSize.Height;

    public int FrameCount => Math.Max(1, Asset.Metadata.FrameCount);

    public string DimensionsText => $"{Width:N0} × {Height:N0} px";

    public string FormatText => Asset.Metadata.FrameCount > 1
        ? $"{Asset.Metadata.BitsPerChannel}-bit · {Asset.Metadata.Channels}通道 · {Asset.Metadata.FrameCount}页"
        : $"{Asset.Metadata.BitsPerChannel}-bit · {Asset.Metadata.Channels}通道";

    public string FileSizeText => FormatBytes(Asset.Fingerprint.ByteLength);

    public string DpiText => Asset.Metadata.DpiX is double dpiX && Asset.Metadata.DpiY is double dpiY
        ? $"{dpiX:0.#}×{dpiY:0.#} dpi"
        : "DPI 未提供";

    public string OmeText => Asset.Metadata.Ome?.Summary ?? "无 OME-XML";

    public string DetailsText => Asset.Metadata.Ome is null
        ? $"{FormatText} · {DpiText} · {FileSizeText}"
        : $"{FormatText} · {DpiText} · {Asset.Metadata.Ome.Summary} · {FileSizeText}";

    public string MeasurementCountText => $"测量表 · {Measurements.Count} 项";

    public string AnalysisCountText => $"图像分析 · {AnalysisResults.Count} 项";

    public string SelectedMeasurementStatusText => SelectedMeasurement is null
        ? "未选择测量对象"
        : $"{SelectedMeasurement.TypeText} · {SelectedMeasurement.ValueText} · " +
          $"{SelectedMeasurement.PixelValueText}" +
          (string.IsNullOrWhiteSpace(SelectedMeasurement.AreaPerimeterText)
              ? string.Empty
              : $" · {SelectedMeasurement.AreaPerimeterText}");

    public string MeasurementSummaryText
    {
        get
        {
            MeasurementStatistics? statistics = MeasurementStatistics.Calculate(GetLengthValues());
            string unit = Calibration.IsCalibrated ? Calibration.Unit : "px";
            return statistics is null
                ? Calibration.IsCalibrated
                    ? "尚无可统计的长度测量"
                    : "未标定 · 结果以 px 显示"
                : $"N {statistics.Count} · Mean {statistics.Mean:0.###} {unit} · " +
                  $"SD {statistics.StandardDeviation:0.###} {unit}";
        }
    }

    public IReadOnlyList<MeasurementHistogramBarViewModel> MeasurementHistogramBars
    {
        get
        {
            MeasurementHistogram? histogram = MeasurementHistogram.Create(GetLengthValues());
            if (histogram is null || histogram.MaximumBinCount == 0)
            {
                return [];
            }

            string unit = Calibration.IsCalibrated ? Calibration.Unit : "px";
            return histogram.Bins
                .Select(bin => new MeasurementHistogramBarViewModel(
                    8 + 44 * bin.Count / (double)histogram.MaximumBinCount,
                    $"{bin.LowerBound:0.###}–{bin.UpperBound:0.###} {unit} · N={bin.Count}",
                    bin.Count))
                .ToArray();
        }
    }

    public Visibility MeasurementHistogramVisibility => MeasurementHistogramBars.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string MeasurementHistogramStatusText
    {
        get
        {
            MeasurementHistogram? histogram = MeasurementHistogram.Create(GetLengthValues());
            if (histogram is null)
            {
                return "添加长度或折线测量后显示分布";
            }

            string unit = Calibration.IsCalibrated ? Calibration.Unit : "px";
            return $"长度分布 · {histogram.Minimum:0.###}–{histogram.Maximum:0.###} {unit} · {histogram.Bins.Count} bins";
        }
    }

    public ScientificMeasurementViewModel AddMeasurement(
        ScientificMeasurementKind kind,
        MeasurementPoint pointA,
        MeasurementPoint pointB,
        MeasurementPoint? pointC = null,
        Guid? id = null,
        string? strokeColor = null,
        double strokeWidthPixels = 3,
        IReadOnlyList<MeasurementPoint>? pathPoints = null,
        ScientificMeasurementVisualStyle? visualStyle = null,
        long? sourceRevision = null)
    {
        var measurement = new ScientificMeasurementViewModel(
            id ?? Guid.NewGuid(),
            Asset.Id,
            DisplayName,
            kind,
            pointA,
            pointB,
            pointC,
            Calibration.Calibration,
            Measurements.Count + 1,
            pathPoints,
            sourceRevision ?? SourceRevision)
        {
            StrokeColor = strokeColor ?? "#FF22C7E8",
            StrokeWidthPixels = strokeWidthPixels,
        };
        if (visualStyle is not null)
        {
            measurement.RestoreVisualStyle(visualStyle);
        }
        measurement.Changed += OnMeasurementChanged;
        Measurements.Add(measurement);
        SelectedMeasurement = measurement;
        NotifyMeasurementCollectionChanged();
        return measurement;
    }

    public void RemoveMeasurement(ScientificMeasurementViewModel measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        int index = Measurements.IndexOf(measurement);
        if (index < 0)
        {
            return;
        }

        measurement.Changed -= OnMeasurementChanged;
        Measurements.RemoveAt(index);
        RenumberMeasurements();
        SelectedMeasurement = Measurements.ElementAtOrDefault(Math.Min(index, Measurements.Count - 1));
        NotifyMeasurementCollectionChanged();
        ScienceEditCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void CancelMeasurement(ScientificMeasurementViewModel measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        int index = Measurements.IndexOf(measurement);
        if (index < 0)
        {
            return;
        }

        measurement.Changed -= OnMeasurementChanged;
        Measurements.RemoveAt(index);
        RenumberMeasurements();
        SelectedMeasurement = Measurements.ElementAtOrDefault(Math.Min(index, Measurements.Count - 1));
        NotifyMeasurementCollectionChanged();
    }

    public void RestoreScience(
        SpatialCalibration calibration,
        double referenceStartX,
        double referenceStartY,
        double referenceEndX,
        double referenceEndY,
        IEnumerable<ScientificMeasurement> measurements,
        IReadOnlyDictionary<Guid, ScientificMeasurementVisualStyle>? styles = null)
    {
        Calibration.Restore(
            calibration,
            referenceStartX,
            referenceStartY,
            referenceEndX,
            referenceEndY);
        foreach (ScientificMeasurementViewModel measurement in Measurements)
        {
            measurement.Changed -= OnMeasurementChanged;
        }

        Measurements.Clear();
        foreach (ScientificMeasurement measurement in measurements)
        {
            ScientificMeasurementVisualStyle style = styles?.GetValueOrDefault(
                measurement.Id) ?? ScientificMeasurementVisualStyle.Default;
            AddMeasurement(
                measurement.Kind,
                measurement.PointA,
                measurement.PointB,
                measurement.PointC,
                measurement.Id,
                style.StrokeColor,
                style.StrokeWidthPixels,
                measurement.PathPoints,
                style,
                measurement.SourceRevision);
        }

        SelectedMeasurement = Measurements.FirstOrDefault();
        NotifyMeasurementCollectionChanged();
    }

    public void AddAnalysisResult(
        ScientificImageAnalysisResult result,
        bool completeEdit = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasValidProvenance || result.SourceAssetId != Asset.Id)
        {
            throw new InvalidDataException("分析结果缺少有效的源素材溯源信息。");
        }

        if (result.SourceRevision != SourceRevision)
        {
            throw new InvalidOperationException(
                $"分析结果基于 source revision {result.SourceRevision}，当前为 {SourceRevision}。");
        }

        AnalysisResults.Add(result.Revalidate(Asset.Id, SourceRevision));
        NotifyAnalysisCollectionChanged();
        if (completeEdit)
        {
            ScienceEditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RestoreAnalysisResults(IEnumerable<ScientificImageAnalysisResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        AnalysisResults.Clear();
        foreach (ScientificImageAnalysisResult result in results)
        {
            AnalysisResults.Add(result.Revalidate(Asset.Id, SourceRevision));
        }

        OnPropertyChanged(nameof(AnalysisCountText));
    }

    public string CreateMeasurementCsv()
    {
        var csv = new StringBuilder();
        csv.AppendLine("Image,ID,Type,Value,Unit,PixelValue,Area,AreaUnit,Perimeter,PerimeterUnit");
        foreach (ScientificMeasurementViewModel measurement in Measurements)
        {
            csv.Append(EscapeCsv(DisplayName)).Append(',')
                .Append(measurement.Number.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(measurement.TypeText)).Append(',')
                .Append(measurement.CsvValue).Append(',')
                .Append(EscapeCsv(measurement.UnitText)).Append(',')
                .Append(measurement.Measurement.PixelValue.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
                .Append(measurement.Measurement.PixelArea > 0
                    ? (measurement.Measurement.PhysicalArea(Calibration.Calibration) ?? measurement.Measurement.PixelArea)
                        .ToString("0.######", CultureInfo.InvariantCulture)
                    : string.Empty).Append(',')
                .Append(measurement.Measurement.PixelArea > 0
                    ? EscapeCsv(Calibration.IsCalibrated ? $"{Calibration.Unit}²" : "px²")
                    : string.Empty).Append(',')
                .Append(measurement.Measurement.PixelArea > 0
                    ? (measurement.Measurement.PhysicalPerimeter(Calibration.Calibration) ?? measurement.Measurement.PixelPerimeter)
                        .ToString("0.######", CultureInfo.InvariantCulture)
                    : string.Empty).Append(',')
                .Append(measurement.Measurement.PixelArea > 0
                    ? EscapeCsv(Calibration.IsCalibrated ? Calibration.Unit : "px")
                    : string.Empty)
                .AppendLine();
        }

        return csv.ToString();
    }
    public BitmapSource GetFramePreview(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        if (_framePreviews.TryGetValue(frameIndex, out BitmapSource? cached))
        {
            return cached;
        }

        BitmapSource loaded = WpfImageFramePreviewLoader.Load(OriginalPath, 1400, frameIndex);
        _framePreviews[frameIndex] = loaded;
        return loaded;
    }

    public string Sha256Short => Asset.Fingerprint.Sha256[..12];

    internal void AcceptRevision(SourceAsset asset, BitmapSource preview)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(preview);
        if (asset.Id != Asset.Id)
        {
            throw new InvalidOperationException("接受源图新版本时必须保留工程内源图 ID。");
        }

        _asset = asset;
        _sourceRevision++;
        _preview = preview;
        _framePreviews.Clear();
        _framePreviews[0] = preview;
        Calibration.RefreshMetadataCalibration(asset.Metadata);
        for (int index = 0; index < AnalysisResults.Count; index++)
        {
            AnalysisResults[index] = AnalysisResults[index].Revalidate(asset.Id, _sourceRevision);
        }
        OnPropertyChanged(nameof(Asset));
        OnPropertyChanged(nameof(SourceRevision));
        OnPropertyChanged(nameof(SourceRevisionText));
        OnPropertyChanged(nameof(LinkStateText));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(OriginalPath));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(DimensionsText));
        OnPropertyChanged(nameof(FormatText));
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(DpiText));
        OnPropertyChanged(nameof(OmeText));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(Sha256Short));
        OnPropertyChanged(nameof(AnalysisCountText));
    }

    internal void RestoreSourceRevision(long sourceRevision)
    {
        _sourceRevision = Math.Max(1, sourceRevision);
        OnPropertyChanged(nameof(SourceRevision));
        OnPropertyChanged(nameof(SourceRevisionText));
    }

    internal void UpdateUsageCount(int usageCount)
    {
        int normalized = Math.Max(0, usageCount);
        if (_usageCount == normalized)
        {
            return;
        }

        _usageCount = normalized;
        OnPropertyChanged(nameof(UsageCount));
        OnPropertyChanged(nameof(UsageText));
    }

    private void OnCalibrationChanged(object? sender, EventArgs e)
    {
        foreach (ScientificMeasurementViewModel measurement in Measurements)
        {
            measurement.RefreshCalibration(Calibration.Calibration);
        }

        OnPropertyChanged(nameof(MeasurementSummaryText));
        NotifyMeasurementHistogramChanged();
        OnPropertyChanged(nameof(SelectedMeasurementStatusText));
        ScienceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnScienceEditCompleted(object? sender, EventArgs e) =>
        ScienceEditCompleted?.Invoke(this, EventArgs.Empty);

    private void OnMeasurementChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(MeasurementSummaryText));
        NotifyMeasurementHistogramChanged();
        OnPropertyChanged(nameof(SelectedMeasurementStatusText));
        ScienceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyMeasurementCollectionChanged()
    {
        OnPropertyChanged(nameof(MeasurementCountText));
        OnPropertyChanged(nameof(MeasurementSummaryText));
        NotifyMeasurementHistogramChanged();
        OnPropertyChanged(nameof(SelectedMeasurementStatusText));
        ScienceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyAnalysisCollectionChanged()
    {
        OnPropertyChanged(nameof(AnalysisCountText));
        AnalysisChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenumberMeasurements()
    {
        for (int index = 0; index < Measurements.Count; index++)
        {
            Measurements[index].Number = index + 1;
        }
    }

    private IEnumerable<double> GetLengthValues() => Measurements
        .Where(measurement => measurement.Kind is
            ScientificMeasurementKind.Length or ScientificMeasurementKind.Polyline)
        .Select(measurement => measurement.NumericValue ?? measurement.Measurement.PixelValue)
        .Where(double.IsFinite);

    private void NotifyMeasurementHistogramChanged()
    {
        OnPropertyChanged(nameof(MeasurementHistogramBars));
        OnPropertyChanged(nameof(MeasurementHistogramVisibility));
        OnPropertyChanged(nameof(MeasurementHistogramStatusText));
    }

    private static string EscapeCsv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
