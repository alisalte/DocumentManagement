using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Dms.Storage.Infrastructure.Processing;

/// <summary>
/// One line of text shaped with HarfBuzz, so Persian letters join and read right to left. A small
/// bidi pass splits the text into right-to-left and left-to-right runs (Latin words, digits) and
/// lays them out in visual order; neutrals take the direction around them.
/// </summary>
internal static class ShapedLine
{
    public static SKTextBlob? Build(string text, SKTypeface typeface, SKFont font, out float width)
    {
        width = 0;
        var runs = Runs(text);
        if (runs.Count == 0)
        {
            return null;
        }

        var baseRtl = runs.FirstOrDefault(run => run.Strong)?.Rtl ?? false;
        if (baseRtl)
        {
            runs.Reverse();
        }

        using var shaper = new SKShaper(typeface);
        using var builder = new SKTextBlobBuilder();
        foreach (var run in runs)
        {
            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf16(run.Text);
            buffer.Direction = run.Rtl ? Direction.RightToLeft : Direction.LeftToRight;
            buffer.GuessSegmentProperties();

            var shaped = shaper.Shape(buffer, width, 0, font);
            var glyphs = shaped.Codepoints.Select(codepoint => (ushort)codepoint).ToArray();
            builder.AddPositionedRun(glyphs, font, shaped.Points);
            width += shaped.Width;
        }

        return builder.Build();
    }

    private sealed record Run(string Text, bool Rtl, bool Strong);

    private static List<Run> Runs(string text)
    {
        // Strong direction per character: true RTL, false LTR, null neutral.
        var classes = text.Select(character => (bool?)(IsRtl(character) ? true : char.IsLetterOrDigit(character) ? false : null)).ToArray();
        var baseRtl = classes.FirstOrDefault(value => value is not null) ?? false;

        var resolved = new bool[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            if (classes[index] is { } strong)
            {
                resolved[index] = strong;
                continue;
            }

            var before = classes.Take(index).LastOrDefault(value => value is not null);
            var after = classes.Skip(index + 1).FirstOrDefault(value => value is not null);
            resolved[index] = before is { } b && after is { } a && a == b ? a : baseRtl;
        }

        var runs = new List<Run>();
        var start = 0;
        for (var index = 1; index <= text.Length; index++)
        {
            if (index == text.Length || resolved[index] != resolved[start])
            {
                var slice = text[start..index];
                runs.Add(new Run(slice, resolved[start], classes.Skip(start).Take(index - start).Any(value => value is not null)));
                start = index;
            }
        }

        return runs;
    }

    // Hebrew, Arabic (with Persian letters), Syriac, Thaana and the Arabic presentation forms.
    // Arabic-Indic digits are numbers, not letters: they stay in a left-to-right run.
    private static bool IsRtl(char character) =>
        character is (>= '֐' and <= 'ࣿ') or (>= 'יִ' and <= '﷿') or (>= 'ﹰ' and <= '﻿')
        && !(character is (>= '٠' and <= '٩') or (>= '۰' and <= '۹'));
}
