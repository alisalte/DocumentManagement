using System.Text;

namespace Dms.Search.Application;

/// <summary>
/// Folds text the way the Persian index does, so a query can match part of a word and the same
/// folding can count how often that phrase occurs in a file.
/// </summary>
public static class SearchText
{
    /// <summary>Shorter than this, an infix match ("در" inside every word) is too noisy.</summary>
    public const int MinInfixLength = 3;

    private static readonly string[] IndexedTextFields =
    [
        "content",
        "content.joined",
        "content.en",
        "title",
        "title.joined",
        "title.en",
        "meta_text",
        "meta_text.joined",
        "description",
        "file_name",
        "tags.text",
    ];

    public static IReadOnlyList<string> Fields => IndexedTextFields;

    /// <summary>
    /// A wildcard against the indexed terms, or null when the token is too short to infix-match.
    /// Latin and digits may be two characters ("14" inside "1403"); Persian stays at three.
    /// </summary>
    public static string? InfixPattern(string token)
    {
        var folded = Fold(token);
        if (!AllowsInfix(folded))
        {
            return null;
        }

        return $"*{EscapeWildcard(folded)}*";
    }

    /// <summary>How many times <paramref name="query"/> occurs in <paramref name="content"/>.</summary>
    public static int CountInContent(string? content, string? query)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var hay = Fold(content);
        var needle = Fold(query);
        if (needle.Length < 2)
        {
            return 0;
        }

        var spaced = CountOf(hay, needle);
        var joinedNeedle = RemoveSpaces(needle);
        if (joinedNeedle.Length < 2)
        {
            return spaced;
        }

        // "می شود" and "می‌شود" are the same phrase once the non-joiner and the space are gone.
        return Math.Max(spaced, CountOf(RemoveSpaces(hay), joinedNeedle));
    }

    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            switch (character)
            {
                case 'ي' or 'ی' or 'ى':
                    builder.Append('ی');
                    break;
                case 'ك' or 'ک':
                    builder.Append('ک');
                    break;
                case 'ۀ' or 'ة':
                    builder.Append('ه');
                    break;
                case 'أ' or 'إ' or 'آ' or 'ٱ' or 'ا':
                    builder.Append('ا');
                    break;
                case '\u200C' or '\u200D' or '\u0640' or '\u200E' or '\u200F' or '\uFEFF':
                    break;
                default:
                    if (character is >= '۰' and <= '۹')
                    {
                        builder.Append((char)('0' + (character - '۰')));
                    }
                    else if (character is >= '٠' and <= '٩')
                    {
                        builder.Append((char)('0' + (character - '٠')));
                    }
                    else if (char.IsWhiteSpace(character))
                    {
                        builder.Append(' ');
                    }
                    else
                    {
                        builder.Append(char.ToLowerInvariant(character));
                    }

                    break;
            }
        }

        return CollapseSpaces(builder.ToString());
    }

    private static bool AllowsInfix(string folded)
    {
        if (folded.Length >= MinInfixLength)
        {
            return true;
        }

        return folded.Length == 2 && folded.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9'));
    }

    private static string EscapeWildcard(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("?", "\\?", StringComparison.Ordinal);

    private static string CollapseSpaces(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pending = false;
        foreach (var character in value)
        {
            if (character == ' ')
            {
                pending = builder.Length > 0;
                continue;
            }

            if (pending)
            {
                builder.Append(' ');
                pending = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string RemoveSpaces(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static int CountOf(string haystack, string needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
