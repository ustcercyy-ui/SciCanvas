using System.Collections.ObjectModel;
using System.IO;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;
using SciCanvas.Imaging;

namespace SciCanvas.Presentation;

public sealed class PublishingPortabilityWorkspaceViewModel : ObservableObject, IDisposable
{
    private FigureCanvasViewModel _figure;
    private readonly Func<FigureExportDocument>? _createExportDocument;
    private JournalExportPreset? _selectedPreset;
    private FontSubstitutionItemViewModel? _selectedSubstitution;
    private string _presetJson = string.Empty;
    private string _requestedFont = string.Empty;
    private string _substituteFont = "Arial";
    private string _statusText = "Preset JSON 与字体替换只作用于当前工程；requested font 永不被改写。";
    private JournalPresetCollisionPolicy _collisionPolicy = JournalPresetCollisionPolicy.RequireDecision;
    private bool _isRefreshingMissingFonts;

    public PublishingPortabilityWorkspaceViewModel(
        FigureCanvasViewModel figure,
        Func<FigureExportDocument>? createExportDocument = null)
    {
        _figure = figure ?? throw new ArgumentNullException(nameof(figure));
        _createExportDocument = createExportDocument;
        _figure.DocumentChanged += OnFigureChanged;
        foreach (JournalExportPreset preset in JournalExportPreset.BuiltIns)
        {
            Presets.Add(preset);
        }


        ExportSelectedPresetCommand = new RelayCommand(ExportSelectedPreset, () => SelectedPreset is not null);
        ExportPresetPackCommand = new RelayCommand(ExportPresetPack, () => Presets.Count > 0);
        PreviewPresetJsonCommand = new RelayCommand(PreviewPresetJson);
        ImportPresetJsonCommand = new RelayCommand(ImportPresetJson);
        SetSubstitutionCommand = new RelayCommand(SetSubstitution);
        RemoveSelectedSubstitutionCommand = new RelayCommand(
            RemoveSelectedSubstitution,
            () => SelectedSubstitution is not null);
        SelectedPreset = Presets.FirstOrDefault();
        RefreshMissingFonts();
    }

    public event EventHandler? Changed;

    public ObservableCollection<JournalExportPreset> Presets { get; } = [];

    public ObservableCollection<MissingFontItemViewModel> MissingFonts { get; } = [];

    public ObservableCollection<FontSubstitutionItemViewModel> Substitutions { get; } = [];

    public IReadOnlyList<string> InstalledFonts => SystemFontCatalog.Instance.InstalledFontFamilies;

    public IReadOnlyList<JournalPresetCollisionPolicy> CollisionPolicyChoices { get; } =
        Enum.GetValues<JournalPresetCollisionPolicy>();

    public JournalExportPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                ExportSelectedPresetCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedPresetSummary));
            }
        }
    }

    public FontSubstitutionItemViewModel? SelectedSubstitution
    {
        get => _selectedSubstitution;
        set
        {
            if (SetProperty(ref _selectedSubstitution, value))
            {
                RemoveSelectedSubstitutionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string PresetJson
    {
        get => _presetJson;
        set => SetProperty(ref _presetJson, value ?? string.Empty);
    }

    public string RequestedFont
    {
        get => _requestedFont;
        set => SetProperty(ref _requestedFont, value ?? string.Empty);
    }

    public string SubstituteFont
    {
        get => _substituteFont;
        set => SetProperty(ref _substituteFont, value ?? string.Empty);
    }

    public JournalPresetCollisionPolicy CollisionPolicy
    {
        get => _collisionPolicy;
        set => SetProperty(ref _collisionPolicy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SelectedPresetSummary => SelectedPreset is null
        ? "未选择 preset"
        : $"{SelectedPreset.FigureWidthMm:0.###} mm · {SelectedPreset.MinimumDpi} dpi · " +
          $"{string.Join(", ", SelectedPreset.AllowedFormats)} · {SelectedPreset.ColorMode}";

    public RelayCommand ExportSelectedPresetCommand { get; }

    public RelayCommand ExportPresetPackCommand { get; }

    public RelayCommand PreviewPresetJsonCommand { get; }

    public RelayCommand ImportPresetJsonCommand { get; }

    public RelayCommand SetSubstitutionCommand { get; }

    public RelayCommand RemoveSelectedSubstitutionCommand { get; }

    public IReadOnlyList<JournalExportPreset> CreatePresetModels() => Presets.ToArray();

    public IReadOnlyList<FontSubstitutionRule> CreateSubstitutionModels() => Substitutions
        .Select(item => item.ToModel())
        .ToArray();

    public ResolvedFigureExportDocument ResolveFonts(FigureExportDocument document) =>
        FigureExportFontResolver.Resolve(document, CreateSubstitutionModels(), SystemFontCatalog.Instance);

    public void Restore(
        IEnumerable<JournalExportPreset> presetSnapshots,
        IEnumerable<FontSubstitutionRule> substitutions)
    {
        JournalExportPreset[] snapshots = (presetSnapshots ?? []).ToArray();
        Presets.Clear();
        foreach (JournalExportPreset preset in JournalExportPreset.BuiltIns
                     .Concat(snapshots)
                     .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            Presets.Add(preset);
        }

        Substitutions.Clear();
        foreach (FontSubstitutionRule rule in (substitutions ?? []).Select(item => item.EnsureValid()))
        {
            Substitutions.Add(new FontSubstitutionItemViewModel(rule));
        }

        SelectedPreset = Presets.FirstOrDefault();
        SelectedSubstitution = Substitutions.FirstOrDefault();
        RefreshMissingFonts();
        StatusText = $"已恢复 {snapshots.Length} 个 preset snapshot 与 {Substitutions.Count} 条字体替换。";
    }

    public void AttachFigure(FigureCanvasViewModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (ReferenceEquals(_figure, figure))
        {
            return;
        }

        _figure.DocumentChanged -= OnFigureChanged;
        _figure = figure;
        _figure.DocumentChanged += OnFigureChanged;
        RefreshMissingFonts();
    }

    public void Dispose() => _figure.DocumentChanged -= OnFigureChanged;

    private void ExportSelectedPreset()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        PresetJson = JournalPresetPortability.ExportPreset(SelectedPreset);
        StatusText = $"已生成 {SelectedPreset.Name} 的 .scicanvas-journal-preset.json 内容。";
    }

    private void ExportPresetPack()
    {
        PresetJson = JournalPresetPortability.ExportPack("SciCanvas Team Preset Pack", Presets);
        StatusText = $"已生成包含 {Presets.Count} 个 preset 的团队 pack JSON。";
    }

    private void PreviewPresetJson()
    {
        try
        {
            IReadOnlyList<JournalPresetImportPreview> preview = JournalPresetPortability.PreviewImport(PresetJson);
            StatusText = string.Join(" | ", preview.Select(item =>
                $"{item.Name}: {item.WidthMm:0.###} mm, {item.Dpi} dpi, {string.Join('/', item.Formats)}, {item.ColorMode}"));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            StatusText = exception.Message;
        }
    }

    private void ImportPresetJson()
    {
        try
        {
            JournalPresetImportResult imported = JournalPresetPortability.Import(Presets, PresetJson, CollisionPolicy);
            Presets.Clear();
            foreach (JournalExportPreset preset in imported.Presets)
            {
                Presets.Add(preset);
            }

            SelectedPreset = Presets.LastOrDefault();
            StatusText = $"已导入 {imported.Preview.Count} 个 preset；替换 {imported.ReplacedIds.Count}，生成新 ID {imported.GeneratedIds.Count}。";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (JournalPresetCollisionException exception)
        {
            StatusText = $"ID 冲突：{string.Join(", ", exception.CollidingIds)}。请选择 GenerateNewId 或 Replace 后再导入。";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            StatusText = exception.Message;
        }
    }

    private void SetSubstitution()
    {
        try
        {
            var rule = new FontSubstitutionRule(RequestedFont, SubstituteFont).EnsureValid();
            if (!SystemFontCatalog.Instance.IsInstalled(rule.SubstituteFontFamily))
            {
                StatusText = $"Substitute font “{rule.SubstituteFontFamily}” 未安装；规则未应用。";
                return;
            }

            FontSubstitutionItemViewModel? existing = Substitutions.FirstOrDefault(item =>
                string.Equals(item.RequestedFontFamily, rule.RequestedFontFamily, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new FontSubstitutionItemViewModel(rule);
                Substitutions.Add(existing);
            }
            else
            {
                existing.SubstituteFontFamily = rule.SubstituteFontFamily;
            }

            SelectedSubstitution = existing;
            RefreshMissingFonts();
            StatusText = $"Project substitution: {rule.RequestedFontFamily} → {rule.SubstituteFontFamily}。Requested style 保持不变。";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException exception)
        {
            StatusText = exception.Message;
        }
    }

    private void RemoveSelectedSubstitution()
    {
        if (SelectedSubstitution is not { } selected)
        {
            return;
        }

        int index = Substitutions.IndexOf(selected);
        Substitutions.Remove(selected);
        SelectedSubstitution = Substitutions.ElementAtOrDefault(Math.Max(0, index - 1));
        RefreshMissingFonts();
        StatusText = $"已移除 {selected.RequestedFontFamily} 的 project substitution。";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshMissingFonts()
    {
        if (_isRefreshingMissingFonts)
        {
            return;
        }

        _isRefreshingMissingFonts = true;
        try
        {
            IReadOnlyList<FontUsage> fontUsages;
            try
            {
                FigureExportDocument document = _createExportDocument?.Invoke() ??
                                                _figure.CreateExportDocument();
                fontUsages = FontUsageCollector.Collect(document);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or ArgumentException or FormatException)
            {
                StatusText = $"字体使用图尚未形成有效 export snapshot：{exception.Message}";
                return;
            }

            MissingFonts.Clear();
            foreach (IGrouping<string, FontUsage> group in fontUsages
                         .Where(usage => !SystemFontCatalog.Instance.IsInstalled(usage.RequestedFont))
                         .GroupBy(usage => usage.RequestedFont, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                string? substitute = Substitutions.FirstOrDefault(item =>
                    string.Equals(item.RequestedFontFamily, group.Key, StringComparison.OrdinalIgnoreCase))
                    ?.SubstituteFontFamily;
                MissingFonts.Add(new MissingFontItemViewModel(
                    group.Key,
                    substitute,
                    string.Join(", ", group.Select(FormatUsage).Distinct(StringComparer.Ordinal))));
            }

            OnPropertyChanged(nameof(InstalledFonts));
        }
        finally
        {
            _isRefreshingMissingFonts = false;
        }
    }

    private static string FormatUsage(FontUsage usage) =>
        $"{usage.UsageKind}" +
        (usage.PanelId is Guid panelId ? $" panel:{panelId}" : string.Empty) +
        (usage.ObjectId is Guid objectId ? $" object:{objectId}" : string.Empty);

    private void OnFigureChanged(object? sender, EventArgs e) => RefreshMissingFonts();
}

public sealed class FontSubstitutionItemViewModel : ObservableObject
{
    private string _substituteFontFamily;

    public FontSubstitutionItemViewModel(FontSubstitutionRule rule)
    {
        rule.EnsureValid();
        RequestedFontFamily = rule.RequestedFontFamily.Trim();
        _substituteFontFamily = rule.SubstituteFontFamily.Trim();
    }

    public string RequestedFontFamily { get; }

    public string SubstituteFontFamily
    {
        get => _substituteFontFamily;
        set => SetProperty(ref _substituteFontFamily, value?.Trim() ?? string.Empty);
    }

    public string Summary => $"{RequestedFontFamily} → {SubstituteFontFamily}";

    public FontSubstitutionRule ToModel() =>
        new FontSubstitutionRule(RequestedFontFamily, SubstituteFontFamily).EnsureValid();
}

public sealed record MissingFontItemViewModel(
    string RequestedFontFamily,
    string? SubstituteFontFamily,
    string UsedBy)
{
    public string Status => string.IsNullOrWhiteSpace(SubstituteFontFamily)
        ? "Missing"
        : $"Missing → {SubstituteFontFamily}";
}
