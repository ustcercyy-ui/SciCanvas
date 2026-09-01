using SciCanvas.Core.Data;

namespace SciCanvas.Persistence;

internal sealed record RawTabularTable(
    TabularDataFormat Format,
    string EncodingName,
    char? Delimiter,
    IReadOnlyList<string> AvailableSheets,
    string? SelectedSheetName,
    string? SelectedRange,
    int HeaderRow,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

internal interface IRawTabularTableReader
{
    Task<RawTabularTable> ReadAsync(
        string sourcePath,
        TabularDataImportOptions options,
        CancellationToken cancellationToken);
}
