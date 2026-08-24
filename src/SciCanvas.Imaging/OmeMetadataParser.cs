using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using SciCanvas.Core.Images;

namespace SciCanvas.Imaging;

internal static class OmeMetadataParser
{
    private const string ImageDescriptionQuery = "/ifd/{ushort=270}";

    public static OmeImageMetadata? TryParse(BitmapFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Metadata is not BitmapMetadata metadata)
        {
            return null;
        }

        string? xml = TryReadDescription(metadata)?.TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            using var reader = XmlReader.Create(
                new StringReader(xml),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 16 * 1024 * 1024,
                });
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            if (document.Root?.Name.LocalName != "OME")
            {
                return null;
            }

            XElement? pixels = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Pixels");
            if (pixels is null)
            {
                return null;
            }

            string dimensionOrder = RequiredAttribute(pixels, "DimensionOrder");
            string pixelType = RequiredAttribute(pixels, "Type");
            int sizeZ = PositiveIntAttribute(pixels, "SizeZ");
            int sizeC = PositiveIntAttribute(pixels, "SizeC");
            int sizeT = PositiveIntAttribute(pixels, "SizeT");
            string[] channels = pixels.Elements()
                .Where(element => element.Name.LocalName == "Channel")
                .Select((element, index) =>
                    OptionalAttribute(element, "Name") ??
                    OptionalAttribute(element, "ID") ??
                    $"Channel {index + 1}")
                .ToArray();

            return new OmeImageMetadata(
                dimensionOrder,
                pixelType,
                sizeZ,
                sizeC,
                sizeT,
                OptionalDoubleAttribute(pixels, "PhysicalSizeX"),
                OptionalDoubleAttribute(pixels, "PhysicalSizeY"),
                OptionalDoubleAttribute(pixels, "PhysicalSizeZ"),
                OptionalAttribute(pixels, "PhysicalSizeXUnit"),
                OptionalAttribute(pixels, "PhysicalSizeYUnit"),
                OptionalAttribute(pixels, "PhysicalSizeZUnit"),
                OptionalDoubleAttribute(pixels, "TimeIncrement"),
                OptionalAttribute(pixels, "TimeIncrementUnit"),
                channels,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))));
        }
        catch (Exception exception) when (
            exception is XmlException or InvalidDataException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static string? TryReadDescription(BitmapMetadata metadata)
    {
        try
        {
            return metadata.ContainsQuery(ImageDescriptionQuery)
                ? metadata.GetQuery(ImageDescriptionQuery) as string
                : null;
        }
        catch (Exception exception) when (
            exception is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string RequiredAttribute(XElement element, string name) =>
        OptionalAttribute(element, name) ??
        throw new InvalidDataException($"OME-XML 缺少 {name}。");

    private static string? OptionalAttribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;

    private static int PositiveIntAttribute(XElement element, string name)
    {
        int value = int.Parse(RequiredAttribute(element, name), System.Globalization.CultureInfo.InvariantCulture);
        return value > 0 ? value : throw new InvalidDataException($"OME-XML 的 {name} 必须为正数。");
    }

    private static double? OptionalDoubleAttribute(XElement element, string name)
    {
        string? value = OptionalAttribute(element, name);
        if (value is null)
        {
            return null;
        }

        double parsed = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        return double.IsFinite(parsed) && parsed > 0 ? parsed : null;
    }
}
