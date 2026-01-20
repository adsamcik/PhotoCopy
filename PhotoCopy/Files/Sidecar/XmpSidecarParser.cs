using System;
using System.IO;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace PhotoCopy.Files.Sidecar;

/// <summary>
/// Parses XMP sidecar files to extract photo/video metadata.
/// </summary>
/// <remarks>
/// XMP files are XML-based and contain EXIF/IPTC metadata in various namespaces.
/// Common sources: Adobe Lightroom, Photoshop, and other photo management tools.
/// </remarks>
public class XmpSidecarParser : ISidecarParser
{
    private readonly ILogger<XmpSidecarParser> _logger;

    // XMP Namespaces
    private static readonly XNamespace RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace ExifNs = "http://ns.adobe.com/exif/1.0/";
    private static readonly XNamespace XmpNs = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace PhotoshopNs = "http://ns.adobe.com/photoshop/1.0/";
    private static readonly XNamespace XmlNs = XNamespace.Xml;

    public XmpSidecarParser(ILogger<XmpSidecarParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CanParse(string extension)
    {
        return string.Equals(extension, ".xmp", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public SidecarMetadata? Parse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("XMP file path is null or empty");
            return null;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("XMP sidecar file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
            var content = File.ReadAllText(filePath);

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("XMP sidecar file is empty: {FilePath}", filePath);
                return null;
            }

            return ParseXmpContent(content, filePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Error reading XMP sidecar file {FilePath}: {Message}", filePath, ex.Message);
            return null;
        }
        catch (System.Xml.XmlException ex)
        {
            _logger.LogWarning("Error parsing XMP sidecar file {FilePath}: {Message}", filePath, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unexpected error parsing XMP sidecar file {FilePath}: {Message}", filePath, ex.Message);
            return null;
        }
    }

    private SidecarMetadata? ParseXmpContent(string content, string filePath)
    {
        var doc = XDocument.Parse(content);

        // Find the rdf:Description element(s)
        var descriptions = doc.Descendants(RdfNs + "Description");

        double? latitude = null;
        double? longitude = null;
        double? altitude = null;
        DateTime? dateTaken = null;
        string? title = null;
        string? description = null;

        foreach (var desc in descriptions)
        {
            // Extract GPS data
            latitude ??= ExtractLatitude(desc);
            longitude ??= ExtractLongitude(desc);
            altitude ??= ExtractAltitude(desc);

            // Extract date
            dateTaken ??= ExtractDate(desc);

            // Extract title and description
            title ??= ExtractRdfAltText(desc, DcNs + "title");
            description ??= ExtractRdfAltText(desc, DcNs + "description");
        }

        // If we couldn't extract any meaningful data, return null
        if (!latitude.HasValue && !dateTaken.HasValue && string.IsNullOrEmpty(title))
        {
            _logger.LogDebug("No meaningful metadata found in XMP sidecar file: {FilePath}", filePath);
            return null;
        }

        // Only return GPS data if both lat and lon are present
        if (latitude.HasValue != longitude.HasValue)
        {
            _logger.LogDebug("Incomplete GPS data in XMP sidecar (latitude or longitude missing): {FilePath}", filePath);
            latitude = null;
            longitude = null;
            altitude = null;
        }

        return new SidecarMetadata
        {
            DateTaken = dateTaken,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Description = string.IsNullOrWhiteSpace(description) ? null : description
        };
    }

    private double? ExtractLatitude(XElement description)
    {
        // Try attribute first
        var latAttr = description.Attribute(ExifNs + "GPSLatitude");
        if (latAttr != null)
        {
            return XmpGpsParser.ParseCoordinate(latAttr.Value, isLongitude: false);
        }

        // Try element
        var latElement = description.Element(ExifNs + "GPSLatitude");
        if (latElement != null)
        {
            return XmpGpsParser.ParseCoordinate(latElement.Value, isLongitude: false);
        }

        return null;
    }

    private double? ExtractLongitude(XElement description)
    {
        // Try attribute first
        var lonAttr = description.Attribute(ExifNs + "GPSLongitude");
        if (lonAttr != null)
        {
            return XmpGpsParser.ParseCoordinate(lonAttr.Value, isLongitude: true);
        }

        // Try element
        var lonElement = description.Element(ExifNs + "GPSLongitude");
        if (lonElement != null)
        {
            return XmpGpsParser.ParseCoordinate(lonElement.Value, isLongitude: true);
        }

        return null;
    }

    private double? ExtractAltitude(XElement description)
    {
        // Try attribute first
        var altAttr = description.Attribute(ExifNs + "GPSAltitude");
        if (altAttr != null)
        {
            return XmpGpsParser.ParseAltitude(altAttr.Value);
        }

        // Try element
        var altElement = description.Element(ExifNs + "GPSAltitude");
        if (altElement != null)
        {
            return XmpGpsParser.ParseAltitude(altElement.Value);
        }

        return null;
    }

    private DateTime? ExtractDate(XElement description)
    {
        // Try xmp:CreateDate first (most common)
        var createDate = ExtractDateFromAttribute(description, XmpNs + "CreateDate")
            ?? ExtractDateFromElement(description, XmpNs + "CreateDate");

        if (createDate.HasValue)
        {
            return createDate;
        }

        // Try photoshop:DateCreated
        var photoshopDate = ExtractDateFromAttribute(description, PhotoshopNs + "DateCreated")
            ?? ExtractDateFromElement(description, PhotoshopNs + "DateCreated");

        if (photoshopDate.HasValue)
        {
            return photoshopDate;
        }

        // Try exif:DateTimeOriginal
        var exifDate = ExtractDateFromAttribute(description, ExifNs + "DateTimeOriginal")
            ?? ExtractDateFromElement(description, ExifNs + "DateTimeOriginal");

        return exifDate;
    }

    private static DateTime? ExtractDateFromAttribute(XElement description, XName attributeName)
    {
        var attr = description.Attribute(attributeName);
        if (attr != null && TryParseDateTime(attr.Value, out var date))
        {
            return date;
        }

        return null;
    }

    private static DateTime? ExtractDateFromElement(XElement description, XName elementName)
    {
        var element = description.Element(elementName);
        if (element != null && TryParseDateTime(element.Value, out var date))
        {
            return date;
        }

        return null;
    }

    private static bool TryParseDateTime(string? value, out DateTime result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // XMP dates are typically in ISO 8601 format
        // Examples: "2024-06-15T14:30:00", "2024-06-15T14:30:00+00:00", "2024-06-15"
        if (DateTime.TryParse(value, out result))
        {
            return true;
        }

        // Try parsing with various ISO formats
        var formats = new[]
        {
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.fffzzz",
            "yyyy-MM-dd",
            "yyyy:MM:dd HH:mm:ss" // EXIF format
        };

        return DateTime.TryParseExact(
            value,
            formats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out result);
    }

    /// <summary>
    /// Extracts text from an RDF Alt structure (used for dc:title, dc:description).
    /// Structure: &lt;dc:title&gt;&lt;rdf:Alt&gt;&lt;rdf:li xml:lang="x-default"&gt;Text&lt;/rdf:li&gt;&lt;/rdf:Alt&gt;&lt;/dc:title&gt;
    /// </summary>
    private string? ExtractRdfAltText(XElement description, XName elementName)
    {
        var element = description.Element(elementName);
        if (element == null)
        {
            return null;
        }

        // Look for rdf:Alt/rdf:li structure
        var alt = element.Element(RdfNs + "Alt");
        if (alt != null)
        {
            // First try to find x-default language
            foreach (var li in alt.Elements(RdfNs + "li"))
            {
                var langAttr = li.Attribute(XmlNs + "lang");
                if (langAttr?.Value == "x-default")
                {
                    return li.Value;
                }
            }

            // Fall back to first li element
            var firstLi = alt.Element(RdfNs + "li");
            if (firstLi != null)
            {
                return firstLi.Value;
            }
        }

        // Maybe it's a simple text element
        if (!string.IsNullOrWhiteSpace(element.Value))
        {
            return element.Value;
        }

        return null;
    }
}
