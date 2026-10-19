using System.Globalization;
using System.Text;
using Dms.Search.Application;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Dms.Search.Infrastructure.Extraction;

/// <summary>One recognised line: the words kept from it, and the text block it belongs to.</summary>
public sealed record OcrLine(int Block, string Text);

/// <summary>The lines one tesseract pass kept, and the median height in pixels of their words.</summary>
public sealed record OcrPass(IReadOnlyList<OcrLine> Lines, double MedianWordHeight);

/// <summary>
/// Turns tesseract's TSV output into text. Words below a confidence threshold are dropped (on a
/// photograph they are mostly shapes read as letters), and lines found only by the sparse pass are
/// added to those of the layout pass.
/// </summary>
public static class OcrText
{
    private const int LevelWord = 5;

    /// <summary>A sparse line is added when at least this share of its words is new.</summary>
    private const double NewWordShare = 0.5;

    public static OcrPass Parse(string tsv, int minConfidence)
    {
        var lines = new List<(int Block, int Paragraph, int Line, List<string> Words)>();
        var heights = new List<int>();
        foreach (var row in tsv.Split('\n'))
        {
            // level page block par line word left top width height conf text
            var cells = row.TrimEnd('\r').Split('\t');
            if (cells.Length < 12
                || !int.TryParse(cells[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
                || level != LevelWord)
            {
                continue;
            }

            var text = cells[11].Trim();
            if (text.Length == 0
                || !double.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence)
                || confidence < minConfidence
                || !text.Any(char.IsLetterOrDigit))
            {
                continue;
            }

            var block = int.Parse(cells[2], CultureInfo.InvariantCulture);
            var paragraph = int.Parse(cells[3], CultureInfo.InvariantCulture);
            var line = int.Parse(cells[4], CultureInfo.InvariantCulture);
            if (lines.Count == 0 || lines[^1].Block != block || lines[^1].Paragraph != paragraph || lines[^1].Line != line)
            {
                lines.Add((block, paragraph, line, []));
            }

            lines[^1].Words.Add(text);
            heights.Add(int.Parse(cells[9], CultureInfo.InvariantCulture));
        }

        heights.Sort();
        return new OcrPass(
            lines
                .Where(line => line.Words.Any(word => word.Count(char.IsLetter) >= 2))
                .Select(line => new OcrLine(line.Block, string.Join(' ', line.Words)))
                .ToList(),
            heights.Count == 0 ? 0 : heights[heights.Count / 2]);
    }

    /// <summary>The layout lines, then every sparse line that is mostly words the layout pass did not read.</summary>
    public static IReadOnlyList<OcrLine> Merge(IReadOnlyList<OcrLine> layout, IReadOnlyList<OcrLine> sparse)
    {
        var known = layout.SelectMany(line => Words(line.Text)).ToHashSet(StringComparer.Ordinal);
        var block = layout.Count == 0 ? 0 : layout.Max(line => line.Block) + 1;
        var merged = layout.ToList();
        foreach (var line in sparse)
        {
            var words = Words(line.Text);
            if (words.Count == 0 || words.Count(word => !known.Contains(word)) < words.Count * NewWordShare)
            {
                continue;
            }

            merged.Add(line with { Block = block });
            known.UnionWith(words);
        }

        return merged;
    }

    /// <summary>One line per recognised line, a blank line between text blocks.</summary>
    public static string Join(IReadOnlyList<OcrLine> lines)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
                if (lines[index].Block != lines[index - 1].Block)
                {
                    builder.Append('\n');
                }
            }

            builder.Append(lines[index].Text);
        }

        return builder.ToString();
    }

    /// <summary>Words as letters and digits only, so «کنید.» and «کنید» are the same word.</summary>
    private static List<string> Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Concat(word.Where(char.IsLetterOrDigit)))
            .Where(word => word.Length > 1)
            .ToList();
}

/// <summary>Prepares an image for tesseract: grayscale on a white background, plus an enlarged copy when small.</summary>
internal static class OcrImage
{
    private const double MaxScale = 3;

    /// <summary>Enlarging by less than this is not worth the extra OCR time.</summary>
    private const double MinScale = 1.2;

    /// <summary>
    /// Luminance. Tesseract binarises a colour image on its own and loses coloured text on a light
    /// background (a blue headline); grayscale keeps it.
    /// </summary>
    private static readonly SKColorFilter Grayscale = SKColorFilter.CreateColorMatrix(
    [
        0.299f, 0.587f, 0.114f, 0, 0,
        0.299f, 0.587f, 0.114f, 0, 0,
        0.299f, 0.587f, 0.114f, 0, 0,
        0, 0, 0, 1, 0,
    ]);

    /// <summary>
    /// A grayscale copy of the image, and an enlarged one when it is small enough to need it. Either
    /// path falls back to the input when the image cannot be decoded here (TIFF): tesseract reads it.
    /// </summary>
    public static (string Original, string? Enlarged) Prepare(string input, string workDir, TesseractOptions options, ILogger logger)
    {
        try
        {
            using var original = SKBitmap.Decode(input);
            if (original is null || original.Width == 0 || original.Height == 0)
            {
                return (input, null);
            }

            var gray = Draw(original, 1, Path.Combine(workDir, "gray.png"));
            if (options.UpscaleShortSide <= 0)
            {
                return (gray, null);
            }

            var area = (double)original.Width * original.Height;
            var scale = Math.Min(
                Math.Min(MaxScale, (double)options.UpscaleShortSide / Math.Min(original.Width, original.Height)),
                Math.Sqrt(Math.Max(options.MaxUpscaledPixels, 1) / area));
            return (gray, scale < MinScale ? null : Draw(original, scale, Path.Combine(workDir, "enlarged.png")));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not prepare {Input} for OCR; using it as is.", input);
            return (input, null);
        }
    }

    private static string Draw(SKBitmap original, double scale, string output)
    {
        var width = (int)Math.Round(original.Width * scale);
        var height = (int)Math.Round(original.Height * scale);
        using var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            // Transparent pixels would otherwise read as black.
            canvas.Clear(SKColors.White);
            using var source = SKImage.FromBitmap(original);
            using var paint = new SKPaint { ColorFilter = Grayscale };
            canvas.DrawImage(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.CatmullRom), paint);
        }

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(output);
        data.SaveTo(file);
        return output;
    }
}
