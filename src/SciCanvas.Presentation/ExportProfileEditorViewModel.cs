using System.IO;
using SciCanvas.Core.Export;
using SciCanvas.Persistence;

namespace SciCanvas.Presentation;

public sealed class ExportProfileEditorViewModel : ObservableObject
{
    private string _name;
    private string _format;
    private int _dpi;
    private double _scale;
    private int? _widthPixels;
    private int? _heightPixels;
    private int _bitDepth;
    private bool _writeProvenance;
    private PdfFontStrategy _pdfFontStrategy;

    public ExportProfileEditorViewModel(FigureExportProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Id = profile.Id;
        _name = profile.Name;
        _format = profile.Format;
        _dpi = profile.Dpi;
        _scale = profile.Scale;
        _widthPixels = profile.WidthPixels;
        _heightPixels = profile.HeightPixels;
        _bitDepth = profile.BitDepth;
        _writeProvenance = profile.WriteProvenance;
        _pdfFontStrategy = profile.PdfFontStrategy;
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value ?? string.Empty)) NotifyValidation(); }
    }

    public string Format
    {
        get => _format;
        set { if (SetProperty(ref _format, value ?? string.Empty)) NotifyValidation(); }
    }

    public int Dpi
    {
        get => _dpi;
        set { if (SetProperty(ref _dpi, value)) NotifyValidation(); }
    }

    public double Scale
    {
        get => _scale;
        set { if (SetProperty(ref _scale, value)) NotifyValidation(); }
    }

    public int? WidthPixels
    {
        get => _widthPixels;
        set { if (SetProperty(ref _widthPixels, value)) NotifyValidation(); }
    }

    public int? HeightPixels
    {
        get => _heightPixels;
        set { if (SetProperty(ref _heightPixels, value)) NotifyValidation(); }
    }

    public int BitDepth
    {
        get => _bitDepth;
        set { if (SetProperty(ref _bitDepth, value)) NotifyValidation(); }
    }

    public bool WriteProvenance
    {
        get => _writeProvenance;
        set { if (SetProperty(ref _writeProvenance, value)) NotifyValidation(); }
    }

    public IReadOnlyList<PdfFontStrategy> PdfFontStrategyChoices { get; } =
        Enum.GetValues<PdfFontStrategy>();

    public PdfFontStrategy PdfFontStrategy
    {
        get => _pdfFontStrategy;
        set { if (SetProperty(ref _pdfFontStrategy, value)) NotifyValidation(); }
    }

    public string PdfFontStrategyDescription => PdfFontStrategy switch
    {
        PdfFontStrategy.OutlineText => "Appearance portable · text converted to outlines.",
        PdfFontStrategy.EmbedSubsetWhenPermitted => "Strict embedding · preflight error when permission or writer support is unavailable.",
        PdfFontStrategy.PreferEmbeddedWithOutlineFallback => "Prefer embedding · explicit QC warning and outline fallback when unavailable.",
        _ => "Unknown PDF font strategy.",
    };

    public string Summary => IsValid
        ? $"{Format.ToUpperInvariant()} · {Dpi} dpi · {BitDepth}-bit · {PdfFontStrategy}"
        : "预设参数无效";

    public bool IsValid => TryCreate(out _, out _);

    public string ValidationMessage => TryCreate(out _, out string? error)
        ? "预设有效；只设置宽或高时自动保持画布比例。"
        : error ?? "预设无效。";

    public FigureExportProfile ToModel()
    {
        if (TryCreate(out FigureExportProfile? profile, out string? error))
        {
            return profile!;
        }

        throw new InvalidDataException(error ?? "导出预设无效。");
    }

    public static ExportProfileEditorViewModel FromSnapshot(ProjectExportProfileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string id = StableIdToProfileKey(snapshot.Id);
        return new ExportProfileEditorViewModel(new FigureExportProfile(
            id,
            snapshot.Name,
            snapshot.Format,
            snapshot.Dpi,
            snapshot.Scale,
            snapshot.WidthPixels,
            snapshot.HeightPixels,
            snapshot.WriteProvenance,
            snapshot.BitDepth ?? 8,
            ParsePdfFontStrategy(snapshot.PdfFontStrategy)));
    }

    private bool TryCreate(out FigureExportProfile? profile, out string? error)
    {
        try
        {
            profile = new FigureExportProfile(
                Id,
                Name,
                Format,
                Dpi,
                Scale,
                WidthPixels,
                HeightPixels,
                WriteProvenance,
                BitDepth,
                PdfFontStrategy);
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            profile = null;
            error = exception.Message;
            return false;
        }
    }

    private void NotifyValidation()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(PdfFontStrategyDescription));
    }

    private static PdfFontStrategy ParsePdfFontStrategy(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "outlinetext" => PdfFontStrategy.OutlineText,
            "embedsubsetwhenpermitted" => PdfFontStrategy.EmbedSubsetWhenPermitted,
            "preferembeddedwithoutlinefallback" => PdfFontStrategy.PreferEmbeddedWithOutlineFallback,
            _ => throw new InvalidDataException($"未知 PDF font strategy：{value}"),
        };

    private static string StableIdToProfileKey(Guid id) => id switch
    {
        var value when value == Guid.Parse("4757F9DE-FE43-47F6-9675-690BE0A431E0") => "main-tiff",
        var value when value == Guid.Parse("B7D1C6D5-4B43-4C36-9A6F-7F6F2F4D5E22") => "supplement-png",
        var value when value == Guid.Parse("F6A3B8E8-9B8D-4BA0-A9D9-5AF1BA58C44F") => "thumbnail-png",
        _ => id.ToString("D"),
    };
}
