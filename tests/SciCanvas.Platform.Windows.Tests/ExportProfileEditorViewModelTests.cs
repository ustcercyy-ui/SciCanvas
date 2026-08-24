using System.IO;
using SciCanvas.Core.Export;
using SciCanvas.Persistence;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ExportProfileEditorViewModelTests
{
    [Fact]
    public void EditorCreatesValidatedCustomSixteenBitProfile()
    {
        var editor = new ExportProfileEditorViewModel(new FigureExportProfile(
            Guid.NewGuid().ToString("D"),
            "期刊主图",
            "tiff",
            600,
            widthPixels: 3600,
            bitDepth: 16));

        FigureExportProfile profile = editor.ToModel();

        Assert.True(editor.IsValid);
        Assert.Equal(600, profile.Dpi);
        Assert.Equal(3600, profile.WidthPixels);
        Assert.Equal(16, profile.BitDepth);
    }

    [Fact]
    public void EditorRejectsSixteenBitPngBeforeBatchExport()
    {
        var editor = new ExportProfileEditorViewModel(new FigureExportProfile(
            Guid.NewGuid().ToString("D"),
            "PNG",
            "png",
            300));
        editor.BitDepth = 16;

        Assert.False(editor.IsValid);
        Assert.Throws<InvalidDataException>(() => editor.ToModel());
    }

    [Fact]
    public void SnapshotRoundTripPreservesEditableFields()
    {
        Guid id = Guid.NewGuid();
        var snapshot = new ProjectExportProfileSnapshot
        {
            Id = id,
            Name = "自定义补充图",
            Format = "png",
            Dpi = 450,
            Scale = 0.75,
            WidthPixels = 2400,
            WriteProvenance = false,
            BitDepth = 8,
        };

        FigureExportProfile profile = ExportProfileEditorViewModel.FromSnapshot(snapshot).ToModel();

        Assert.Equal(id.ToString("D"), profile.Id);
        Assert.Equal("自定义补充图", profile.Name);
        Assert.Equal(450, profile.Dpi);
        Assert.Equal(0.75, profile.Scale);
        Assert.Equal(2400, profile.WidthPixels);
        Assert.False(profile.WriteProvenance);
    }
}
