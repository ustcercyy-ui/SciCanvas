namespace SciCanvas.Core.Cropping;

public sealed record CropValidationResult(bool IsValid, string? Message)
{
    public static CropValidationResult Success { get; } = new(true, null);

    public static CropValidationResult Failure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new CropValidationResult(false, message);
    }
}

