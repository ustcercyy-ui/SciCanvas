using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Imaging;

public sealed class WpfAssistedRegionAnalyzer : IAssistedRegionAnalyzer
{
    public const string AnalyzerVersion = "scicanvas.connected-components.v1";

    public Task<AssistedRegionAnalysisResult> AnalyzeAsync(
        SourceAsset source,
        AssistedRegionAnalysisOptions options,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid)
        {
            throw new ArgumentException("区域分析参数无效。", nameof(options));
        }

        if (options.RegionOfInterest.Right > source.Metadata.PixelSize.Width ||
            options.RegionOfInterest.Bottom > source.Metadata.PixelSize.Height)
        {
            throw new InvalidDataException("分析 ROI 超出源图范围。");
        }

        return Task.Run(
            () => AnalyzeCore(source, options, frameIndex, cancellationToken),
            cancellationToken);
    }

    private static AssistedRegionAnalysisResult AnalyzeCore(
        SourceAsset source,
        AssistedRegionAnalysisOptions options,
        int frameIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BitmapSource cropped = WpfFigureExporter.LoadExactCrop(
            source.OriginalPath,
            options.RegionOfInterest,
            frameIndex);
        BitmapSource gray = cropped.Format == PixelFormats.Gray8
            ? cropped
            : new FormatConvertedBitmap(cropped, PixelFormats.Gray8, null, 0);
        gray.Freeze();
        int width = gray.PixelWidth;
        int height = gray.PixelHeight;
        byte[] pixels = new byte[checked(width * height)];
        gray.CopyPixels(pixels, width, 0);
        byte threshold = options.UseAutomaticThreshold
            ? CalculateOtsuThreshold(pixels)
            : (byte)Math.Clamp((int)Math.Round(options.ThresholdNormalized * byte.MaxValue), 0, 255);
        bool[] foreground = new bool[pixels.Length];
        long foregroundCount = 0;
        for (int index = 0; index < pixels.Length; index++)
        {
            bool isForeground = options.DetectDarkRegions
                ? pixels[index] <= threshold
                : pixels[index] > threshold;
            foreground[index] = isForeground;
            if (isForeground)
            {
                foregroundCount++;
            }
        }

        bool[] visited = new bool[pixels.Length];
        var candidates = new List<AssistedRegionCandidate>();
        var queue = new Queue<int>();
        for (int seed = 0; seed < foreground.Length; seed++)
        {
            if (!foreground[seed] || visited[seed])
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            visited[seed] = true;
            queue.Enqueue(seed);
            int area = 0;
            int perimeter = 0;
            long intensitySum = 0;
            long sumX = 0;
            long sumY = 0;
            int minX = width;
            int minY = height;
            int maxX = 0;
            int maxY = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int x = current % width;
                int y = current / width;
                area++;
                intensitySum += pixels[current];
                sumX += x;
                sumY += y;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                perimeter += BoundaryEdges(foreground, width, height, x, y);

                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        int nextX = x + offsetX;
                        int nextY = y + offsetY;
                        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
                        {
                            continue;
                        }

                        int next = nextY * width + nextX;
                        if (foreground[next] && !visited[next])
                        {
                            visited[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            if (area < options.MinimumAreaPixels)
            {
                continue;
            }

            int componentWidth = maxX - minX + 1;
            int componentHeight = maxY - minY + 1;
            double aspectRatio = Math.Max(componentWidth, componentHeight) /
                                 (double)Math.Max(1, Math.Min(componentWidth, componentHeight));
            if (options.RequiresElongatedShape && aspectRatio < 3)
            {
                continue;
            }

            candidates.Add(new AssistedRegionCandidate(
                candidates.Count + 1,
                new(
                    options.RegionOfInterest.X + minX,
                    options.RegionOfInterest.Y + minY,
                    componentWidth,
                    componentHeight),
                options.RegionOfInterest.X + sumX / (double)area,
                options.RegionOfInterest.Y + sumY / (double)area,
                area,
                perimeter,
                intensitySum / (double)area / byte.MaxValue,
                aspectRatio));
        }

        AssistedRegionCandidate[] ordered = candidates
            .OrderByDescending(candidate => candidate.AreaPixels)
            .Take(options.MaximumCandidates)
            .Select((candidate, index) => candidate with { Id = index + 1 })
            .ToArray();
        return new AssistedRegionAnalysisResult(
            options,
            ordered,
            threshold / (double)byte.MaxValue,
            foregroundCount,
            pixels.LongLength,
            AnalyzerVersion,
            DateTimeOffset.UtcNow);
    }

    private static int BoundaryEdges(bool[] foreground, int width, int height, int x, int y)
    {
        int edges = 0;
        if (x == 0 || !foreground[y * width + x - 1]) edges++;
        if (x == width - 1 || !foreground[y * width + x + 1]) edges++;
        if (y == 0 || !foreground[(y - 1) * width + x]) edges++;
        if (y == height - 1 || !foreground[(y + 1) * width + x]) edges++;
        return edges;
    }

    private static byte CalculateOtsuThreshold(byte[] pixels)
    {
        long[] histogram = new long[256];
        foreach (byte pixel in pixels)
        {
            histogram[pixel]++;
        }

        long total = pixels.LongLength;
        double sum = 0;
        for (int level = 0; level < histogram.Length; level++)
        {
            sum += level * histogram[level];
        }

        long backgroundWeight = 0;
        double backgroundSum = 0;
        double maximumVariance = -1;
        int threshold = 0;
        for (int level = 0; level < histogram.Length; level++)
        {
            backgroundWeight += histogram[level];
            if (backgroundWeight == 0)
            {
                continue;
            }

            long foregroundWeight = total - backgroundWeight;
            if (foregroundWeight == 0)
            {
                break;
            }

            backgroundSum += level * histogram[level];
            double backgroundMean = backgroundSum / backgroundWeight;
            double foregroundMean = (sum - backgroundSum) / foregroundWeight;
            double variance = backgroundWeight * (double)foregroundWeight *
                              Math.Pow(backgroundMean - foregroundMean, 2);
            if (variance > maximumVariance)
            {
                maximumVariance = variance;
                threshold = level;
            }
        }

        return (byte)threshold;
    }
}
