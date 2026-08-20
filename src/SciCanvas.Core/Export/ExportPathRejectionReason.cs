namespace SciCanvas.Core.Export;

public enum ExportPathRejectionReason
{
    InvalidPath,
    SameAsSourcePath,
    SameAsSourceFile,
    TargetIsDirectory
}

