using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Cropping;

public static class CropBoundsValidator
{
    public static CropValidationResult Validate(PixelRect64 crop, PixelSize64 sourceSize)
    {
        if (crop.Right > sourceSize.Width)
        {
            return CropValidationResult.Failure(
                $"裁剪区域右边界 {crop.Right} px 超出源图宽度 {sourceSize.Width} px。");
        }

        if (crop.Bottom > sourceSize.Height)
        {
            return CropValidationResult.Failure(
                $"裁剪区域下边界 {crop.Bottom} px 超出源图高度 {sourceSize.Height} px。");
        }

        return CropValidationResult.Success;
    }
}

