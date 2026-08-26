using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using SciCanvas.Core.Science;

namespace SciCanvas.Presentation;

public static class AnalysisTableXlsxWriter
{
    public static void WriteNew(
        string targetPath,
        IEnumerable<ScientificImageAnalysisResult> results)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(results);
        ScientificImageAnalysisResult[] materialized = results.ToArray();
        bool created = false;
        try
        {
            using var output = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None);
            created = true;
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
            WriteTextEntry(archive, "[Content_Types].xml", ContentTypesXml);
            WriteTextEntry(archive, "_rels/.rels", RootRelationshipsXml);
            WriteTextEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
            WriteWorksheet(archive, materialized);
        }
        catch
        {
            if (created)
            {
                try
                {
                    File.Delete(targetPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            throw;
        }
    }

    private static void WriteWorksheet(
        ZipArchive archive,
        IReadOnlyCollection<ScientificImageAnalysisResult> results)
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            "xl/worksheets/sheet1.xml",
            CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            CloseOutput = false,
        });
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        writer.WriteStartElement("sheetData", SpreadsheetNamespace);
        WriteRow(writer, 1, ScientificAnalysisTable.Headers.Cast<object?>().ToArray());

        int rowNumber = 2;
        foreach (IReadOnlyList<object?> row in ScientificAnalysisTable.CreateRows(results))
        {
            WriteRow(writer, rowNumber++, row);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteRow(XmlWriter writer, int rowNumber, IReadOnlyList<object?> values)
    {
        writer.WriteStartElement("row", SpreadsheetNamespace);
        writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
        for (int column = 0; column < values.Count; column++)
        {
            WriteCell(writer, CellReference(column, rowNumber), values[column]);
        }

        writer.WriteEndElement();
    }

    private static void WriteCell(XmlWriter writer, string reference, object? value)
    {
        switch (value)
        {
            case double number when double.IsFinite(number):
                WriteNumberCell(writer, reference, number);
                break;
            case float number when float.IsFinite(number):
                WriteNumberCell(writer, reference, number);
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                WriteNumberCell(
                    writer,
                    reference,
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0");
                break;
            case DateTimeOffset timestamp:
                WriteInlineStringCell(writer, reference, timestamp.ToString("O", CultureInfo.InvariantCulture));
                break;
            default:
                WriteInlineStringCell(writer, reference, value?.ToString() ?? string.Empty);
                break;
        }
    }

    private static void WriteInlineStringCell(XmlWriter writer, string reference, string value)
    {
        writer.WriteStartElement("c", SpreadsheetNamespace);
        writer.WriteAttributeString("r", reference);
        writer.WriteAttributeString("t", "inlineStr");
        writer.WriteStartElement("is", SpreadsheetNamespace);
        writer.WriteElementString("t", SpreadsheetNamespace, value);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteNumberCell(XmlWriter writer, string reference, double value) =>
        WriteNumberCell(
            writer,
            reference,
            value.ToString("0.###############", CultureInfo.InvariantCulture));

    private static void WriteNumberCell(XmlWriter writer, string reference, string value)
    {
        writer.WriteStartElement("c", SpreadsheetNamespace);
        writer.WriteAttributeString("r", reference);
        writer.WriteAttributeString("t", "n");
        writer.WriteElementString("v", SpreadsheetNamespace, value);
        writer.WriteEndElement();
    }

    private static string CellReference(int zeroBasedColumn, int row)
    {
        int value = zeroBasedColumn + 1;
        Span<char> buffer = stackalloc char[8];
        int position = buffer.Length;
        while (value > 0)
        {
            value--;
            buffer[--position] = (char)('A' + value % 26);
            value /= 26;
        }

        return $"{new string(buffer[position..])}{row}";
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;
    private const string RootRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;
    private const string WorkbookXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets><sheet name="Analyses" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;
    private const string WorkbookRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;
}
