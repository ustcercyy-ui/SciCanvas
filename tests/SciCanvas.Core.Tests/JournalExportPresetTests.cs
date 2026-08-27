using SciCanvas.Core.Export;

namespace SciCanvas.Core.Tests;

public sealed class JournalExportPresetTests
{
    [Fact]
    public void BuiltInsExposeFourPublisherNeutralSubmissionShapes()
    {
        Assert.Equal(4, JournalExportPreset.BuiltIns.Count);
        Assert.Contains(JournalExportPreset.BuiltIns, preset => preset.Name == "Single Column");
        Assert.Contains(JournalExportPreset.BuiltIns, preset => preset.Name == "Double Column");
        Assert.Contains(JournalExportPreset.BuiltIns, preset => preset.Name == "Full Page");
        Assert.Contains(JournalExportPreset.BuiltIns, preset =>
            preset.Name == "High Resolution Line Art" && preset.MinimumDpi == 1200);
    }

    [Fact]
    public void CreateProfileConvertsMillimetersAndPreservesConstraints()
    {
        JournalExportPreset preset = JournalExportPreset.BuiltIns.Single(item => item.Id == "generic-single-column");

        FigureExportProfile profile = preset.CreateProfile();

        Assert.Equal(1051, profile.WidthPixels);
        Assert.Null(profile.HeightPixels);
        Assert.Equal(300, profile.Dpi);
        Assert.Equal("tiff", profile.Format);
    }

    [Fact]
    public void PreferredFormatMustBeAllowed()
    {
        Assert.Throws<ArgumentException>(() => new JournalExportPreset(
            "bad", "Bad", 89, null, 300, "tiff", ["png"], "RGB"));
    }
}
