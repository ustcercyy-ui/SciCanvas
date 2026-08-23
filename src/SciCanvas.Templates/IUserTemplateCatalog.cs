namespace SciCanvas.Templates;

public interface IUserTemplateCatalog
{
    IReadOnlyList<FigureTemplateDefinition> LoadInstalled();

    FigureTemplateDefinition ImportFromFile(string sourcePath);
}
