using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SciCanvas.Core.Data;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class TabularDataImporterTests
{
    [Fact]
    public async Task Csv_PreviewsBomDelimiterNumericTypesAndUnitsBeforeConfirmedImport()
    {
        using var workspace = new TestWorkspace();
        string csv = "Strain (%),Stress (MPa),Specimen\r\n0.1,\"1,234.5\",S-01\r\n0.2,1300,S-02\r\n";
        string path = workspace.CreateFile(
            "tensile.csv",
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)]);
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(path));
        var importer = new TabularDataImporter();

        TabularDataImportPreview preview = await importer.PreviewAsync(
            path,
            new TabularDataImportOptions { PreviewRowCount = 1 });

        Assert.Equal(TabularDataFormat.Csv, preview.Format);
        Assert.Equal("UTF-8 BOM", preview.EncodingName);
        Assert.Equal(',', preview.Delimiter);
        Assert.Equal(2, preview.TotalDataRowCount);
        Assert.Single(preview.FirstRows);
        Assert.Collection(
            preview.SuggestedColumns,
            column =>
            {
                Assert.Equal("Strain", column.Name);
                Assert.Equal("%", column.Unit);
                Assert.Equal(TabularDataType.Numeric, column.DataType);
            },
            column =>
            {
                Assert.Equal("Stress", column.Name);
                Assert.Equal("MPa", column.Unit);
                Assert.Equal(TabularDataType.Numeric, column.DataType);
            },
            column => Assert.Equal(TabularDataType.Text, column.DataType));
        DataColumn[] confirmedColumns = preview.SuggestedColumns
            .Select((column, index) => column with
            {
                Role = index switch
                {
                    0 => DataColumnRole.X,
                    1 => DataColumnRole.Y,
                    _ => DataColumnRole.Label,
                },
            })
            .ToArray();

        TabularDataAsset asset = await importer.ImportAsync(
            preview,
            new TabularDataImportConfirmation("Tensile experiment", confirmedColumns));

        Assert.Equal(2, asset.Rows.Count);
        Assert.Equal(1234.5, asset.Rows[0].Values[1].NumericValue);
        Assert.Equal("1,234.5", asset.Rows[0].Values[1].RawText);
        Assert.Equal(DataColumnRole.X, asset.Columns[0].Role);
        Assert.Equal(path, asset.SourcePath);
        Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Tsv_DetectsTabAndUsesSelectedHeaderRowWithInvariantNumbers()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.CreateFile(
            "temperature.tsv",
            Encoding.UTF8.GetBytes(
                "Instrument export\r\nTime [s]\tTemperature (°C)\r\n0\t20.25\r\n1\t21.50\r\n"));
        var importer = new TabularDataImporter();

        TabularDataImportPreview preview = await importer.PreviewAsync(
            path,
            new TabularDataImportOptions { HeaderRow = 2 });

        Assert.Equal(TabularDataFormat.Tsv, preview.Format);
        Assert.Equal('\t', preview.Delimiter);
        Assert.Equal(2, preview.HeaderRow);
        Assert.Equal("Time", preview.SuggestedColumns[0].Name);
        Assert.Equal("s", preview.SuggestedColumns[0].Unit);
        Assert.Equal("Temperature", preview.SuggestedColumns[1].Name);
        Assert.Equal("°C", preview.SuggestedColumns[1].Unit);
        Assert.All(preview.SuggestedColumns, column =>
            Assert.Equal(TabularDataType.Numeric, column.DataType));
    }

    [Fact]
    public async Task Import_RejectsSourceChangedAfterPreview()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.CreateFile(
            "values.csv",
            Encoding.UTF8.GetBytes("X,Y\n1,2\n"));
        var importer = new TabularDataImporter();
        TabularDataImportPreview preview = await importer.PreviewAsync(path);
        await File.AppendAllTextAsync(path, "3,4\n", Encoding.UTF8);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            importer.ImportAsync(preview, new TabularDataImportConfirmation("Changed")));

        Assert.Contains("自预览后已改变", exception.Message);
    }

    [Fact]
    public async Task Import_RejectsConfirmedNumericColumnWhenSourceContainsText()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.CreateFile(
            "mixed.csv",
            Encoding.UTF8.GetBytes("Category\nA\nB\n"));
        var importer = new TabularDataImporter();
        TabularDataImportPreview preview = await importer.PreviewAsync(path);
        DataColumn forcedNumeric = preview.SuggestedColumns[0] with
        {
            DataType = TabularDataType.Numeric,
            Role = DataColumnRole.Y,
        };

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            importer.ImportAsync(
                preview,
                new TabularDataImportConfirmation("Invalid", [forcedNumeric])));

        Assert.Contains("无法按 Numeric 解析", exception.Message);
    }

    [Fact]
    public async Task Xlsx_SelectsSheetRangeAndHeaderWithoutExcelInstallation()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "experiments.xlsx");
        CreateWorkbook(path);
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(path));
        var importer = new TabularDataImporter();

        IReadOnlyList<string> discoveredSheets = await importer.DiscoverSheetsAsync(path);

        TabularDataImportPreview preview = await importer.PreviewAsync(
            path,
            new TabularDataImportOptions
            {
                SheetName = "Temperature",
                SelectedRange = "B2:D4",
                HeaderRow = 1,
            });

        Assert.Equal(["Overview", "Temperature"], discoveredSheets);
        Assert.Equal(TabularDataFormat.Xlsx, preview.Format);
        Assert.Equal(["Overview", "Temperature"], preview.AvailableSheets);
        Assert.Equal("Temperature", preview.SelectedSheetName);
        Assert.Equal("B2:D4", preview.SelectedRange);
        Assert.Equal(2, preview.TotalDataRowCount);
        Assert.Equal("Time", preview.SuggestedColumns[0].Name);
        Assert.Equal("s", preview.SuggestedColumns[0].Unit);
        Assert.Equal("Temperature", preview.SuggestedColumns[1].Name);
        Assert.Equal("°C", preview.SuggestedColumns[1].Unit);
        Assert.Equal(TabularDataType.Text, preview.SuggestedColumns[2].DataType);

        TabularDataAsset asset = await importer.ImportAsync(
            preview,
            new TabularDataImportConfirmation("Temperature curve"));

        Assert.Equal(21.25, asset.Rows[1].Values[1].NumericValue);
        Assert.Equal("B", asset.Rows[1].Values[2].RawText);
        Assert.Equal("Temperature", asset.ImportMetadata.SheetName);
        Assert.Equal("B2:D4", asset.ImportMetadata.SelectedRange);
        Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task TextImport_RejectsUtf16InsteadOfGuessingEncoding()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.CreateFile(
            "utf16.csv",
            Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("X,Y\n1,2\n")).ToArray());
        var importer = new TabularDataImporter();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            importer.PreviewAsync(path));

        Assert.Contains("仅支持 UTF-8", exception.Message);
    }

    private static void CreateWorkbook(string path)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Overview" sheetId="1" r:id="rId1"/>
                <sheet name="Temperature" sheetId="2" r:id="rId2"/>
              </sheets>
            </workbook>
            """);
        WriteEntry(
            archive,
            "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
            </Relationships>
            """);
        WriteEntry(
            archive,
            "xl/worksheets/sheet1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Overview</t></is></c></row></sheetData>
            </worksheet>
            """);
        WriteEntry(
            archive,
            "xl/sharedStrings.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="3" uniqueCount="3">
              <si><t>Label</t></si>
              <si><t>A</t></si>
              <si><t>B</t></si>
            </sst>
            """);
        WriteEntry(
            archive,
            "xl/worksheets/sheet2.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="2">
                  <c r="B2" t="inlineStr"><is><t>Time (s)</t></is></c>
                  <c r="C2" t="inlineStr"><is><t>Temperature [°C]</t></is></c>
                  <c r="D2" t="s"><v>0</v></c>
                </row>
                <row r="3"><c r="B3"><v>0</v></c><c r="C3"><v>20.5</v></c><c r="D3" t="s"><v>1</v></c></row>
                <row r="4"><c r="B4"><v>1</v></c><c r="C4"><v>21.25</v></c><c r="D4" t="s"><v>2</v></c></row>
              </sheetData>
            </worksheet>
            """);
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
