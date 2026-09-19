namespace Dms.Storage.Application;

/// <summary>
/// Content type detection from the leading bytes.
///
/// The client's Content-Type is recorded but never trusted: it is trivially forged, and the value
/// decides how a preview renders later. Anything unrecognised stays application/octet-stream,
/// which is safe: the archive must accept arbitrary binaries, it just will not claim to know them.
/// </summary>
public static class MimeSniffer
{
    /// <summary>Enough for every signature below, including the OLE2 and Zip probes.</summary>
    public const int SampleSize = 512;

    private const string Fallback = "application/octet-stream";

    private static readonly (byte[] Magic, int Offset, string MimeType)[] Signatures =
    [
        ([0x25, 0x50, 0x44, 0x46], 0, "application/pdf"),                         // %PDF
        ([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], 0, "application/x-ole-storage"), // legacy Office
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 0, "image/png"),
        ([0xFF, 0xD8, 0xFF], 0, "image/jpeg"),
        ([0x47, 0x49, 0x46, 0x38], 0, "image/gif"),
        ([0x42, 0x4D], 0, "image/bmp"),
        ([0x49, 0x49, 0x2A, 0x00], 0, "image/tiff"),
        ([0x4D, 0x4D, 0x00, 0x2A], 0, "image/tiff"),
        ([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07], 0, "application/vnd.rar"),
        ([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], 0, "application/x-7z-compressed"),
        ([0x1F, 0x8B], 0, "application/gzip"),
        ([0x66, 0x74, 0x79, 0x70], 4, "video/mp4"),                               // ....ftyp
        ([0x49, 0x44, 0x33], 0, "audio/mpeg"),
        ([0x4F, 0x67, 0x67, 0x53], 0, "application/ogg"),
        ([0x52, 0x49, 0x46, 0x46], 0, "audio/wav"),
        ([0x41, 0x43, 0x31, 0x30], 0, "image/vnd.dwg"),                           // AC10.. DWG
        ([0x7B, 0x5C, 0x72, 0x74, 0x66], 0, "application/rtf"),
    ];

    /// <summary>Zip based Office formats, distinguished by what the archive contains.</summary>
    private static readonly (string Marker, string MimeType)[] OpenXmlMarkers =
    [
        ("word/", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        ("xl/", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        ("ppt/", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        ("META-INF/mozilla.rsa", "application/x-xpinstall"),
    ];

    public static string Detect(ReadOnlySpan<byte> sample, string fileName, string? declaredMimeType)
    {
        if (sample.Length >= 4 && sample[0] == 0x50 && sample[1] == 0x4B && (sample[2] == 0x03 || sample[2] == 0x05))
        {
            return DetectZipFlavour(sample, fileName);
        }

        foreach (var (magic, offset, mimeType) in Signatures)
        {
            if (StartsWith(sample, magic, offset))
            {
                // Legacy Office containers all share the OLE2 header, so the extension decides.
                return mimeType == "application/x-ole-storage" ? DetectOleFlavour(fileName) : mimeType;
            }
        }

        if (LooksLikeText(sample))
        {
            return FromTextExtension(fileName);
        }

        // Nothing recognised: trust the extension only for types that carry no signature at all.
        return ExtensionOnlyTypes.TryGetValue(Extension(fileName), out var byExtension)
            ? byExtension
            : Fallback;
    }

    private static readonly Dictionary<string, string> ExtensionOnlyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dxf"] = "image/vnd.dxf",
        [".dwg"] = "image/vnd.dwg",
        [".step"] = "model/step",
        [".stp"] = "model/step",
        [".iges"] = "model/iges",
        [".msg"] = "application/vnd.ms-outlook",
    };

    private static string DetectZipFlavour(ReadOnlySpan<byte> sample, string fileName)
    {
        var text = System.Text.Encoding.ASCII.GetString(sample);
        foreach (var (marker, mimeType) in OpenXmlMarkers)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
            {
                return mimeType;
            }
        }

        return Extension(fileName) switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            _ => "application/zip",
        };
    }

    private static string DetectOleFlavour(string fileName) => Extension(fileName) switch
    {
        ".xls" => "application/vnd.ms-excel",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".msg" => "application/vnd.ms-outlook",
        _ => "application/msword",
    };

    private static string FromTextExtension(string fileName) => Extension(fileName) switch
    {
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".xml" => "text/xml",
        ".html" or ".htm" => "text/html",
        ".eml" => "message/rfc822",
        ".md" => "text/markdown",
        _ => "text/plain",
    };

    private static bool StartsWith(ReadOnlySpan<byte> sample, ReadOnlySpan<byte> magic, int offset) =>
        sample.Length >= offset + magic.Length && sample.Slice(offset, magic.Length).SequenceEqual(magic);

    /// <summary>UTF-8 text heuristic: no NUL bytes and no stray control characters.</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty)
        {
            return false;
        }

        foreach (var value in sample)
        {
            if (value == 0)
            {
                return false;
            }

            if (value < 0x09 || (value > 0x0D && value < 0x20 && value != 0x1B))
            {
                return false;
            }
        }

        return true;
    }

    private static string Extension(string fileName)
    {
        var index = fileName.LastIndexOf('.');
        return index < 0 ? string.Empty : fileName[index..];
    }
}
