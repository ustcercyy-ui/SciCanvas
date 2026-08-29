using System.IO;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Imaging;

public sealed class WpfAssistedRegionAnalyzer : IAssistedRegionAnalyzer
{
    public const string AnalyzerVersion = "scicanvas.connected-components.v2";

    public Task<AssistedRegionAnalysisResult> AnalyzeAsync(
        SourceAsset source,
        AssistedRegionAnalysisOptions options,
        int frameIndex = 0,
        CancellationToken cancellationToken = default,
        long sourceRevision = 1,
        ImageAnalysisChannel channel = ImageAnalysisChannel.Luminance)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid)
        {
            throw new ArgumentException("区域分析参数无效。", nameof(options));
        }


        if (sourceRevision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        }

        if (options.RegionOfInterest.Right > source.Metadata.PixelSize.Width ||
            options.RegionOfInterest.Bottom > source.Metadata.PixelSize.Height)
        {
            throw new InvalidDataException("分析 ROI 超出源图范围。");
        }

        return Task.Run(
            () => AnalyzeCore(
                source,
                options,
                frameIndex,
                sourceRevision,
                channel,
                cancellationToken),
            cancellationToken);
    }

    private static AssistedRegionAnalysisResult AnalyzeCore(
        SourceAsset source,
        AssistedRegionAnalysisOptions options,
        int frameIndex,
        long sourceRevision,
        ImageAnalysisChannel channel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ScientificPixelBuffer pixelBuffer = WpfScientificPixelReader.ReadRegion(
            source,
            options.RegionOfInterest,
            frameIndex,
            channel,
            cancellationToken);
        int width = pixelBuffer.Width;
        int height = pixelBuffer.Height;
        IReadOnlyList<double> pixels = pixelBuffer.Values;
        double threshold = options.UseAutomaticThreshold
            ? CalculateOtsuThreshold(
                pixels,
                pixelBuffer.MaximumValue,
                pixelBuffer.SourceBitDepth == 16 ? 4096 : 256)
            : options.ThresholdNormalized * pixelBuffer.MaximumValue;
        bool[] foreground = new bool[pixels.Count];
        long foregroundCount = 0;
        for (int index = 0; index < pixels.Count; index++)
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

        bool[] visited = new bool[pixels.Count];
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
            double intensitySum = 0;
            long sumX = 0;
            long sumY = 0;
            var boundaryPoints = new List<ComponentPoint>();
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
                int boundaryEdges = BoundaryEdges(foreground, width, height, x, y);
                perimeter += boundaryEdges;
                if (boundaryEdges > 0)
                {
                    AddPixelCorners(boundaryPoints, x, y);
                }

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

            ComponentPoint[] hull = CreateConvexHull(boundaryPoints);
            (double feretMaximum, double feretMinimum) = CalculateFeretDiameters(hull);

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
                intensitySum / area / pixelBuffer.MaximumValue,
                aspectRatio)
            {
                RawMeanIntensity = intensitySum / area,
                FeretMaximumPixels = feretMaximum,
                FeretMinimumPixels = feretMinimum,
            });
        }

        AssistedRegionCandidate[] ordered = candidates
            .OrderByDescending(candidate => candidate.AreaPixels)
            .Select((candidate, index) => candidate with { Id = index + 1 })
            .ToArray();
        return new AssistedRegionAnalysisResult(
            options,
            ordered,
            threshold / pixelBuffer.MaximumValue,
            foregroundCount,
            pixels.Count)
        {
            Id = Guid.NewGuid(),
            SourceAssetId = source.Id,
            SourceRevision = sourceRevision,
            FrameIndex = frameIndex,
            Channel = channel,
            AnalyzerId = AnalyzerVersion,
            AnalyzedAt = DateTimeOffset.UtcNow,
            SourceBitDepth = pixelBuffer.SourceBitDepth,
        };
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

    private static void AddPixelCorners(ICollection<ComponentPoint> points, int x, int y)
    {
        points.Add(new ComponentPoint(x, y));
        points.Add(new ComponentPoint(x + 1, y));
        points.Add(new ComponentPoint(x + 1, y + 1));
        points.Add(new ComponentPoint(x, y + 1));
    }

    private static ComponentPoint[] CreateConvexHull(IEnumerable<ComponentPoint> points)
    {
        ComponentPoint[] ordered = points
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToArray();
        if (ordered.Length <= 2)
        {
            return ordered;
        }

        var hull = new List<ComponentPoint>(ordered.Length * 2);
        foreach (ComponentPoint point in ordered)
        {
            while (hull.Count >= 2 &&
                   Cross(hull[^2], hull[^1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        int lowerCount = hull.Count;
        for (int index = ordered.Length - 2; index >= 0; index--)
        {
            ComponentPoint point = ordered[index];
            while (hull.Count > lowerCount &&
                   Cross(hull[^2], hull[^1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }

    private static (double Maximum, double Minimum) CalculateFeretDiameters(
        IReadOnlyList<ComponentPoint> hull)
    {
        if (hull.Count == 0)
        {
            return (1, 1);
        }

        if (hull.Count == 1)
        {
            return (1, 1);
        }

        if (hull.Count == 2)
        {
            double distance = Distance(hull[0], hull[1]);
            return (distance, distance);
        }

        int antipodal = 1;
        double maximum = 0;
        double minimum = double.PositiveInfinity;
        for (int index = 0; index < hull.Count; index++)
        {
            int nextIndex = (index + 1) % hull.Count;
            ComponentPoint edgeStart = hull[index];
            ComponentPoint edgeEnd = hull[nextIndex];
            while (TriangleAreaTwice(
                       edgeStart,
                       edgeEnd,
                       hull[(antipodal + 1) % hull.Count]) >
                   TriangleAreaTwice(edgeStart, edgeEnd, hull[antipodal]) + 1e-12)
            {
                antipodal = (antipodal + 1) % hull.Count;
            }

            maximum = Math.Max(maximum, Distance(edgeStart, hull[antipodal]));
            maximum = Math.Max(maximum, Distance(edgeEnd, hull[antipodal]));
            double edgeLength = Distance(edgeStart, edgeEnd);
            if (edgeLength > 0)
            {
                minimum = Math.Min(
                    minimum,
                    TriangleAreaTwice(edgeStart, edgeEnd, hull[antipodal]) / edgeLength);
            }
        }

        return (maximum, minimum);
    }

    private static double Cross(ComponentPoint first, ComponentPoint second, ComponentPoint third) =>
        (second.X - first.X) * (third.Y - first.Y) -
        (second.Y - first.Y) * (third.X - first.X);

    private static double TriangleAreaTwice(
        ComponentPoint first,
        ComponentPoint second,
        ComponentPoint third) => Math.Abs(Cross(first, second, third));

    private static double Distance(ComponentPoint first, ComponentPoint second)
    {
        double deltaX = first.X - second.X;
        double deltaY = first.Y - second.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static double CalculateOtsuThreshold(
        IReadOnlyList<double> pixels,
        double maximumValue,
        int binCount)
    {
        long[] histogram = new long[binCount];
        foreach (double pixel in pixels)
        {
            int bin = Math.Clamp(
                (int)Math.Floor(pixel / maximumValue * (binCount - 1)),
                0,
                binCount - 1);
            histogram[bin]++;
        }

        long total = pixels.Count;
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

        return threshold / (double)(binCount - 1) * maximumValue;
    }

    private readonly record struct ComponentPoint(double X, double Y);
}
