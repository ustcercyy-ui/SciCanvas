namespace SciCanvas.Core.Science;

public sealed record MeasurementHistogramBin(
    double LowerBound,
    double UpperBound,
    int Count)
{
    public bool IsValid =>
        double.IsFinite(LowerBound) &&
        double.IsFinite(UpperBound) &&
        UpperBound >= LowerBound &&
        Count >= 0;
}

public sealed record MeasurementHistogram(
    IReadOnlyList<MeasurementHistogramBin> Bins,
    int SampleCount,
    double Minimum,
    double Maximum)
{
    public int MaximumBinCount => Bins.Count == 0 ? 0 : Bins.Max(bin => bin.Count);

    public static MeasurementHistogram? Create(
        IEnumerable<double> values,
        int? requestedBinCount = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] samples = values.Where(double.IsFinite).OrderBy(value => value).ToArray();
        if (samples.Length == 0)
        {
            return null;
        }

        double minimum = samples[0];
        double maximum = samples[^1];
        if (minimum == maximum)
        {
            return new MeasurementHistogram(
                [new MeasurementHistogramBin(minimum, maximum, samples.Length)],
                samples.Length,
                minimum,
                maximum);
        }

        int binCount = requestedBinCount ?? (int)Math.Ceiling(Math.Sqrt(samples.Length));
        binCount = Math.Clamp(binCount, 1, 20);
        double binWidth = (maximum - minimum) / binCount;
        var counts = new int[binCount];
        foreach (double sample in samples)
        {
            int index = sample == maximum
                ? binCount - 1
                : Math.Clamp((int)((sample - minimum) / binWidth), 0, binCount - 1);
            counts[index]++;
        }

        MeasurementHistogramBin[] bins = Enumerable.Range(0, binCount)
            .Select(index => new MeasurementHistogramBin(
                minimum + index * binWidth,
                index == binCount - 1 ? maximum : minimum + (index + 1) * binWidth,
                counts[index]))
            .ToArray();
        return new MeasurementHistogram(bins, samples.Length, minimum, maximum);
    }
}
