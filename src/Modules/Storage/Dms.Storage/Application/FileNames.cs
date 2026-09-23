using System.Globalization;
using System.Text;

namespace Dms.Storage.Application;

/// <summary>
/// Filenames are metadata, never identity and never part of a storage path. They still have to be
/// safe to store and to echo back in a Content-Disposition header.
/// </summary>
public static class FileNames
{
    public const int MaxLength = 255;

    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "unnamed";
        }

        // Take the leaf only: "../../etc/passwd" and "C:\\secrets\\x.pdf" both collapse safely.
        var leaf = fileName.Replace('\\', '/');
        var lastSlash = leaf.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            leaf = leaf[(lastSlash + 1)..];
        }

        // NFC keeps Persian and other composed text stable across systems.
        leaf = leaf.Normalize(NormalizationForm.FormC);

        var builder = new StringBuilder(leaf.Length);
        foreach (var character in leaf)
        {
            // Persian writes the half-space (ZWNJ) and ZWJ inside ordinary words, so they stay.
            // Every other format character goes, in particular the bidi overrides that can make
            // "gpj.exe" display as "exe.jpg".
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (character is not ('\u200C' or '\u200D')
                && category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate)
            {
                continue;
            }

            builder.Append(character == '\u0000' ? '_' : character);
        }

        var cleaned = builder.ToString().Trim().TrimStart('.');
        if (cleaned.Length == 0)
        {
            return "unnamed";
        }

        return cleaned.Length <= MaxLength ? cleaned : Truncate(cleaned);
    }

    /// <summary>Keeps the extension when shortening, because previews key off it.</summary>
    private static string Truncate(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0 || fileName.Length - dot > 16)
        {
            return fileName[..MaxLength];
        }

        var extension = fileName[dot..];
        return string.Concat(fileName.AsSpan(0, MaxLength - extension.Length), extension);
    }

    /// <summary>
    /// Server-generated object key. Nothing from the request reaches the path, which rules out
    /// traversal and key injection by construction.
    /// </summary>
    public static string BuildObjectKey(string prefix, Guid id, DateTimeOffset now) =>
        $"{prefix}/{now:yyyy}/{now:MM}/{id:N}";
}
