namespace SciCanvas.Core.Cropping;

public sealed record CropPreset
{
    public CropPreset(Guid id, string name, long width, long height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Id = id;
        Name = name;
        Width = width;
        Height = height;
    }

    public Guid Id { get; }

    public string Name { get; }

    public long Width { get; }

    public long Height { get; }

    public string Unit => "px";
}

