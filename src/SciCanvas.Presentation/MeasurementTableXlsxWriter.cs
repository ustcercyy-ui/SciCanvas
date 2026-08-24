using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SciCanvas.Presentation;

public static class MeasurementTableXlsxWriter
{
    private static readonly string[] Headers =
    [
        "Image", "ID", "Type", "Value", "Unit", "PixelValue",
        "Area", "AreaUnit", "Perimeter", "PerimeterUnit",
    ];

    public static void WriteNew(string targetPath, SourceAssetItemViewModel source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(source);
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
            WriteWorksheet(archive, source);
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

    private static void WriteWorksheet(ZipArchive archive, SourceAssetItemViewModel source)
    {
        ZipArchiveEntry entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
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
        writer.WriteStartElement("row", SpreadsheetNamespace);
        writer.WriteAttributeString("r", "1");
        for (int column = 0; column < Headers.Length; column++)
        {
            WriteInlineStringCell(writer, CellReference(column, 1), Headers[column]);
        }

        writer.WriteEndElement();
        int rowNumber = 2;
        foreach (ScientificMeasurementViewModel measurement in source.Measurements)
        {
            var model = measurement.Measurement;
            bool hasRegion = model.PixelArea > 0;
            double? area = hasRegion
                ? model.PhysicalArea(source.Calibration.Calibration) ?? model.PixelArea
                : null;
            double? perimeter = hasRegion
                ? model.PhysicalPerimeter(source.Calibration.Calibration) ?? model.PixelPerimeter
                : null;
            string lengthUnit = source.Calibration.IsCalibrated ? source.Calibration.Unit : "px";
            writer.WriteStartElement("row", SpreadsheetNamespace);
            writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
            WriteInlineStringCell(writer, CellReference(0, rowNumber), source.DisplayName);
            WriteNumberCell(writer, CellReference(1, rowNumber), measurement.Number);
            WriteInlineStringCell(writer, CellReference(2, rowNumber), measurement.TypeText);
            WriteNumberCell(writer, CellReference(3, rowNumber), measurement.NumericValue ?? model.PixelValue);
            WriteInlineStringCell(writer, CellReference(4, rowNumber), measurement.UnitText);
            WriteNumberCell(writer, CellReference(5, rowNumber), model.PixelValue);
            WriteOptionalNumberCell(writer, CellReference(6, rowNumber), area);
            WriteInlineStringCell(writer, CellReference(7, rowNumber), hasRegion ? $"{lengthUnit}²" : string.Empty);
            WriteOptionalNumberCell(writer, CellReference(8, rowNumber), perimeter);
            WriteInlineStringCell(writer, CellReference(9, rowNumber), hasRegion ? lengthUnit : string.Empty);
            writer.WriteEndElement();
            rowNumber++;
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteInlineStringCell(XmlWriter writer, string reference, string value)
    {
        writer.WriteStartElement("c", SpreadsheetNamespace);
        writer.WriteAttributeString("r", reference);
        writer.WriteAttributeString("t", "inlineStr");
        writer.WriteStartElement("is", SpreadsheetNamespace);
        writer.WriteElementString("t", SpreadsheetNamespace, value ?? string.Empty);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteNumberCell(XmlWriter writer, string reference, double value)
    {
        writer.WriteStartElement("c", SpreadsheetNamespace);
        writer.WriteAttributeString("r", reference);
        writer.WriteAttributeString("t", "n");
        writer.WriteElementString("v", SpreadsheetNamespace, value.ToString("0.###############", CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private static void WriteOptionalNumberCell(XmlWriter writer, string reference, double? value)
    {
        if (value.HasValue)
        {
            WriteNumberCell(writer, reference, value.Value);
        }
        else
        {
            WriteInlineStringCell(writer, reference, string.Empty);
        }
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
          <sheets><sheet name="Measurements" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;
    private const string WorkbookRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;
}
