using SciCanvas.Core.Geometry;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class EditorHistoryManagerTests
{
    [Fact]
    public void Record_CapsUndoHistoryAtOneHundredEntries()
    {
        var history = new EditorHistoryManager(100);
        history.Reset(CreateSnapshot(0), markSaved: true);

        for (int index = 1; index <= 105; index++)
        {
            history.Record(CreateSnapshot(index), canCoalesce: false);
        }

        Assert.Equal(100, history.UndoCount);
        Assert.True(history.CanUndo);
        Assert.True(history.IsDirty);
    }

    [Fact]
    public void Record_ContinuousCoordinateChangesMergeIntoOneUndoStep()
    {
        var history = new EditorHistoryManager(100, TimeSpan.FromMinutes(1));
        history.Reset(CreateSnapshot(0), markSaved: true);

        for (int index = 1; index <= 40; index++)
        {
            history.Record(CreateSnapshot(index), canCoalesce: true);
        }

        Assert.Equal(1, history.UndoCount);
        EditorHistorySnapshot restored = Assert.IsType<EditorHistorySnapshot>(history.Undo());
        Assert.Equal(0, restored.ActiveCrop!.Value.X);
        Assert.False(history.IsDirty);

        EditorHistorySnapshot redone = Assert.IsType<EditorHistorySnapshot>(history.Redo());
        Assert.Equal(40, redone.ActiveCrop!.Value.X);
        Assert.True(history.IsDirty);
    }

    private static EditorHistorySnapshot CreateSnapshot(long cropX) => new(
        "materials.multiscale-morphology.nature-double",
        [],
        null,
        new PixelRect64(cropX, 0, 1, 1),
        true,
        true,
        WorkspaceMode.Crop,
        "#FFFFFFFF",
        true,
        true,
        "lowercase",
        SciCanvas.Core.Export.FigureGlobalStyle.Default,
        [],
        null,
        [],
        null,
        null,
        true,
        12,
        24,
        300,
        [],
        [],
        [],
        [],
        []);
}
