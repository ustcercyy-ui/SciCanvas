using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class TemplateLayoutEngineTests
{
    [Fact]
    public void BuiltInMorphologyTemplate_ProducesExpectedNatureLikeCanvas()
    {
        FigureTemplateDefinition template = new BuiltInTemplateCatalog().LoadAll().Single(
            item => item.Id == "materials.multiscale-morphology.nature-double");

        TemplateCanvasLayout layout = TemplateLayoutEngine.CreateLayout(template);

        Assert.Equal("materials.multiscale-morphology.nature-double", layout.TemplateId);
        Assert.Equal(2161, layout.WidthPixels);
        Assert.Equal(2008, layout.HeightPixels);
        Assert.Equal(300, layout.Dpi);
        Assert.Equal(5, layout.Slots.Count);
        Assert.All(layout.Slots, slot =>
        {
            Assert.True(slot.PixelRect.Right <= layout.WidthPixels);
            Assert.True(slot.PixelRect.Bottom <= layout.HeightPixels);
        });
    }

    [Fact]
    public void BuiltInMorphologyTemplate_HasUniqueLabelsAndSlots()
    {
        FigureTemplateDefinition template = new BuiltInTemplateCatalog().LoadAll().Single(
            item => item.Id == "materials.multiscale-morphology.nature-double");
        TemplateCanvasLayout layout = TemplateLayoutEngine.CreateLayout(template);

        Assert.Equal(layout.Slots.Count, layout.Slots.Select(slot => slot.Id).Distinct().Count());
        Assert.Equal(layout.Slots.Count, layout.Slots.Select(slot => slot.Label).Distinct().Count());
    }

    [Fact]
    public void BuiltInCatalog_AllTemplatesAreUniqueAndInsideCanvas()
    {
        IReadOnlyList<FigureTemplateDefinition> templates = new BuiltInTemplateCatalog().LoadAll();

        Assert.Equal(6, templates.Count);
        Assert.Equal(templates.Count, templates.Select(template => template.Id).Distinct().Count());
        foreach (FigureTemplateDefinition template in templates)
        {
            TemplateCanvasLayout layout = TemplateLayoutEngine.CreateLayout(template);
            Assert.NotEmpty(layout.Slots);
            Assert.All(layout.Slots, slot =>
            {
                Assert.True(slot.PixelRect.X >= 0);
                Assert.True(slot.PixelRect.Y >= 0);
                Assert.True(slot.PixelRect.Right <= layout.WidthPixels);
                Assert.True(slot.PixelRect.Bottom <= layout.HeightPixels);
            });
        }
    }

    [Fact]
    public void BuiltInCatalog_CoversCoreMaterialsEvidenceLayouts()
    {
        IReadOnlyList<FigureTemplateDefinition> templates = new BuiltInTemplateCatalog().LoadAll();
        string[] ids = templates.Select(template => template.Id).ToArray();

        Assert.Contains("materials.energy-storage-electrochemistry.nature-double", ids);
        Assert.Contains("materials.phase-structure-mechanism.nature-double", ids);
        Assert.Contains("materials.mechanics-fracture.nature-double", ids);
        Assert.All(templates, template =>
        {
            Assert.Equal("Arial", template.LabelStyle.FontFamily);
            Assert.InRange(template.LabelStyle.FontSizePt, 5, 7);
        });
    }
}
