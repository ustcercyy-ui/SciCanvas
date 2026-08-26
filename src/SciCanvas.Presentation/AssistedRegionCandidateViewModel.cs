using SciCanvas.Core.Science;

namespace SciCanvas.Presentation;

public sealed class AssistedRegionCandidateViewModel : ObservableObject
{
    private readonly SpatialCalibration? _calibration;
    private readonly AssistedRegionMode _mode;
    private bool _isAccepted = true;
    private bool _isCommitted;

    public AssistedRegionCandidateViewModel(
        AssistedRegionCandidate candidate,
        SpatialCalibration? calibration,
        AssistedRegionMode mode)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        _calibration = calibration;
        _mode = mode;
    }

    public event EventHandler? Changed;

    public AssistedRegionCandidate Candidate { get; }

    public int Id => Candidate.Id;

    public long X => Candidate.Bounds.X;

    public long Y => Candidate.Bounds.Y;

    public long Width => Candidate.Bounds.Width;

    public long Height => Candidate.Bounds.Height;

    public bool IsAccepted
    {
        get => _isAccepted;
        set
        {
            if (IsCommitted)
            {
                return;
            }

            if (SetProperty(ref _isAccepted, value))
            {
                OnPropertyChanged(nameof(OverlayStroke));
                OnPropertyChanged(nameof(OverlayFill));
                OnPropertyChanged(nameof(DecisionText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsCommitted
    {
        get => _isCommitted;
        private set
        {
            if (SetProperty(ref _isCommitted, value))
            {
                OnPropertyChanged(nameof(OverlayStroke));
                OnPropertyChanged(nameof(OverlayFill));
                OnPropertyChanged(nameof(DecisionText));
            }
        }
    }

    public string OverlayStroke => IsCommitted
        ? "#FF75D9AA"
        : IsAccepted ? "#FFFFD166" : "#FFEF6B73";

    public string OverlayFill => IsCommitted
        ? "#3375D9AA"
        : IsAccepted ? "#22FFD166" : "#22EF6B73";

    public string DecisionText => IsCommitted ? "已写入测量" : IsAccepted ? "接受" : "拒绝";

    public string AreaText
    {
        get
        {
            double area = _calibration?.IsValid == true
                ? Candidate.AreaPixels * _calibration.UnitsPerPixelX * _calibration.UnitsPerPixelY
                : Candidate.AreaPixels;
            string unit = _calibration?.IsValid == true ? $"{_calibration.Unit}²" : "px²";
            return $"{area:0.###} {unit}";
        }
    }

    public string SizeText
    {
        get
        {
            bool horizontalMajor = Candidate.Bounds.Width >= Candidate.Bounds.Height;
            double pixels = _mode switch
            {
                AssistedRegionMode.DarkCracks => Math.Max(Candidate.Bounds.Width, Candidate.Bounds.Height),
                AssistedRegionMode.BrightLamellae => Math.Min(Candidate.Bounds.Width, Candidate.Bounds.Height),
                _ => Candidate.EquivalentDiameterPixels,
            };
            string prefix = _mode switch
            {
                AssistedRegionMode.DarkCracks => "L ",
                AssistedRegionMode.BrightLamellae => "W ",
                _ => "Ø ",
            };
            string unit = "px";
            if (_calibration?.IsValid == true)
            {
                pixels *= _mode switch
                {
                    AssistedRegionMode.DarkCracks when horizontalMajor => _calibration.UnitsPerPixelX,
                    AssistedRegionMode.DarkCracks => _calibration.UnitsPerPixelY,
                    AssistedRegionMode.BrightLamellae when horizontalMajor => _calibration.UnitsPerPixelY,
                    AssistedRegionMode.BrightLamellae => _calibration.UnitsPerPixelX,
                    _ => Math.Sqrt(_calibration.UnitsPerPixelX * _calibration.UnitsPerPixelY),
                };
                unit = _calibration.Unit;
            }

            return $"{prefix}{pixels:0.###} {unit}";
        }
    }

    public string ShapeText =>
        $"AR {Candidate.AspectRatio:0.##} · C {Candidate.Circularity:0.###} · " +
        $"Fmax {Candidate.FeretMaximumPixels:0.##} px · Iraw {Candidate.RawMeanIntensity:0.###}";

    public void MarkCommitted() => IsCommitted = true;
}
