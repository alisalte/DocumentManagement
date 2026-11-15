using System.Globalization;
using System.Text;
using Dms.SharedKernel;

namespace Dms.DocumentTypes.Contracts;

/// <summary>
/// The shape of a document's indicator (registry) number, for example <c>{TYPE}-{YEAR}-{SEQ:5}</c>
/// → <c>CTR-1405-00045</c>. Text outside braces is copied as is (prefix and separators).
/// Lives in the contracts so the Documents module can format numbers without the DocumentTypes domain.
/// </summary>
/// <remarks>
/// Tokens: <c>{TYPE}</c> document type code, <c>{YEAR}</c> Jalali fiscal year of the document,
/// <c>{CAT}</c> category code, <c>{SEQ:n}</c> sequence padded to n digits (1–10). Every pattern
/// needs exactly one sequence and at least one year: the counter restarts each year, so a pattern
/// without the year would repeat numbers (BR-13).
/// </remarks>
public sealed class IndicatorPattern
{
    public const int MaxLength = 60;
    public const int MaxSequenceDigits = 10;

    /// <summary>Applied to every document type that has no pattern of its own.</summary>
    public const string Default = "{TYPE}-{YEAR}-{SEQ:5}";

    private readonly IReadOnlyList<Part> _parts;

    private IndicatorPattern(string text, IReadOnlyList<Part> parts)
    {
        Text = text;
        _parts = parts;
    }

    public string Text { get; }

    public bool HasTypeCode => _parts.Any(part => part.Kind == PartKind.Type);

    public static Result<IndicatorPattern> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Invalid("The pattern is empty.");
        }

        text = text.Trim();
        if (text.Length > MaxLength)
        {
            return Invalid($"The pattern is limited to {MaxLength} characters.");
        }

        var parts = new List<Part>();
        var literal = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '}')
            {
                return Invalid("A closing brace has no opening brace.");
            }

            if (character != '{')
            {
                literal.Append(character);
                index++;
                continue;
            }

            var end = text.IndexOf('}', index + 1);
            if (end < 0)
            {
                return Invalid("An opening brace is never closed.");
            }

            if (literal.Length > 0)
            {
                parts.Add(new Part(PartKind.Literal, literal.ToString(), 0));
                literal.Clear();
            }

            var token = text[(index + 1)..end].Trim().ToUpperInvariant();
            var part = ParseToken(token);
            if (part is null)
            {
                return Invalid($"Unknown part {{{token}}}. Use {{TYPE}}, {{YEAR}}, {{CAT}} or {{SEQ:n}}.");
            }

            parts.Add(part);
            index = end + 1;
        }

        if (literal.Length > 0)
        {
            parts.Add(new Part(PartKind.Literal, literal.ToString(), 0));
        }

        var sequences = parts.Count(part => part.Kind == PartKind.Sequence);
        if (sequences != 1)
        {
            return Invalid("The pattern needs exactly one {SEQ:n}.");
        }

        if (parts.All(part => part.Kind != PartKind.Year))
        {
            return Invalid("The pattern needs {YEAR}: the counter restarts every fiscal year.");
        }

        return Result.Success(new IndicatorPattern(Canonical(parts), parts));
    }

    /// <summary>Builds the number. Digits are always Latin; the UI localizes them for display.</summary>
    public string Format(string typeCode, int fiscalYear, string? categoryCode, long sequence)
    {
        var builder = new StringBuilder();
        foreach (var part in _parts)
        {
            builder.Append(part.Kind switch
            {
                PartKind.Literal => part.Value,
                PartKind.Type => typeCode,
                PartKind.Year => fiscalYear.ToString(CultureInfo.InvariantCulture),
                PartKind.Category => categoryCode ?? string.Empty,
                PartKind.Sequence => sequence.ToString(CultureInfo.InvariantCulture).PadLeft(part.Digits, '0'),
                _ => string.Empty,
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// The pattern with the type code filled in. Two document types whose resolved patterns are
    /// equal would produce the same numbers from their separate counters (BR-16).
    /// </summary>
    public string Resolve(string typeCode) =>
        string.Concat(_parts.Select(part => part.Kind == PartKind.Type ? typeCode : part.Kind == PartKind.Literal ? part.Value : part.Token))
            .ToUpperInvariant();

    private static Part? ParseToken(string token)
    {
        switch (token)
        {
            case "TYPE":
                return new Part(PartKind.Type, string.Empty, 0);
            case "YEAR":
                return new Part(PartKind.Year, string.Empty, 0);
            case "CAT":
                return new Part(PartKind.Category, string.Empty, 0);
        }

        if (!token.StartsWith("SEQ:", StringComparison.Ordinal)
            || !int.TryParse(token.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var digits)
            || digits is < 1 or > MaxSequenceDigits)
        {
            return null;
        }

        return new Part(PartKind.Sequence, string.Empty, digits);
    }

    private static string Canonical(IEnumerable<Part> parts) =>
        string.Concat(parts.Select(part => part.Kind == PartKind.Literal ? part.Value : part.Token));

    private static Result<IndicatorPattern> Invalid(string message) =>
        Result.Failure<IndicatorPattern>(Error.Validation("indicator_pattern.invalid", message));

    private enum PartKind
    {
        Literal,
        Type,
        Year,
        Category,
        Sequence,
    }

    private sealed record Part(PartKind Kind, string Value, int Digits)
    {
        public string Token => Kind switch
        {
            PartKind.Type => "{TYPE}",
            PartKind.Year => "{YEAR}",
            PartKind.Category => "{CAT}",
            PartKind.Sequence => $"{{SEQ:{Digits.ToString(CultureInfo.InvariantCulture)}}}",
            _ => string.Empty,
        };
    }
}
