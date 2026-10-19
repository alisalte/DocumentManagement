using System.Diagnostics;
using System.Text;
using Dms.Search.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Search.Infrastructure.Extraction;

/// <summary>
/// Shells out to the system <c>tesseract</c> binary with <c>fas+eng</c> (section 8.1, risk R2).
/// Keeps OCR out of process so a hung page cannot take down the worker. Pages are read in
/// grayscale; small letters are read again from an enlarged copy, and a sparse-text pass picks up
/// labels and captions outside the main layout (posters, photos).
/// </summary>
public sealed class TesseractCliOcrEngine(
    IOptions<SearchOptions> options,
    ILogger<TesseractCliOcrEngine> logger) : IOcrEngine
{
    private readonly SearchOptions _options = options.Value;

    public bool IsEnabled =>
        _options.Tesseract.Enabled
        && !string.IsNullOrWhiteSpace(_options.Tesseract.Command)
        && CommandExists(_options.Tesseract.Command);

    public async Task<string> RecogniseAsync(Stream image, string mimeType, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return string.Empty;
        }

        var extension = ExtensionFor(mimeType);
        var work = Directory.CreateTempSubdirectory("dms-ocr-");
        try
        {
            var input = Path.Combine(work.FullName, "page" + extension);
            await using (var file = File.Create(input))
            {
                await image.CopyToAsync(file, cancellationToken);
            }

            var tesseract = _options.Tesseract;
            var (page, enlarged) = OcrImage.Prepare(input, work.FullName, tesseract, logger);

            // Page layout first (paragraphs, columns). Small letters are read again from the
            // enlarged copy; tesseract misses text only a few pixels high.
            var layoutTsv = await RunTsvAsync(page, work.FullName, "layout", 3, cancellationToken);
            if (layoutTsv is null)
            {
                return string.Empty;
            }

            var layout = OcrText.Parse(layoutTsv, tesseract.MinWordConfidence);
            if (enlarged is not null
                && layout.MedianWordHeight < tesseract.SmallTextHeight
                && await RunTsvAsync(enlarged, work.FullName, "layout-enlarged", 3, cancellationToken) is { } enlargedTsv)
            {
                layout = OcrText.Parse(enlargedTsv, tesseract.MinWordConfidence);
            }

            // Then scattered text the layout pass skips: labels, cards, captions on posters and photos.
            var lines = layout.Lines;
            if (tesseract.SparseTextPass
                && await RunTsvAsync(enlarged ?? page, work.FullName, "sparse", 11, cancellationToken) is { } sparseTsv)
            {
                lines = OcrText.Merge(lines, OcrText.Parse(sparseTsv, tesseract.SparseMinWordConfidence).Lines);
            }

            return OcrText.Join(lines);
        }
        finally
        {
            try
            {
                work.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of temp OCR pages.
            }
        }
    }

    /// <summary>Runs one tesseract pass with word-level TSV output (text, position and confidence).</summary>
    private async Task<string?> RunTsvAsync(string page, string workDir, string name, int pageSegmentation, CancellationToken cancellationToken)
    {
        var outputBase = Path.Combine(workDir, name);
        var args = $"\"{page}\" \"{outputBase}\" -l {_options.OcrLanguages} --psm {pageSegmentation} tsv";
        var (exit, stderr) = await RunAsync(
            _options.Tesseract.Command,
            args,
            _options.Tesseract.TimeoutSeconds,
            cancellationToken);

        var tsvPath = outputBase + ".tsv";
        if (exit != 0 || !File.Exists(tsvPath))
        {
            logger.LogWarning("tesseract --psm {Mode} failed (exit {Exit}): {Error}", pageSegmentation, exit, stderr.Trim());
            return null;
        }

        return await File.ReadAllTextAsync(tsvPath, Encoding.UTF8, cancellationToken);
    }

    public static bool CommandExists(string command)
    {
        if (Path.IsPathRooted(command))
        {
            return File.Exists(command);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtensionFor(string mimeType) =>
        mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "image/tiff" => ".tif",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".png",
        };

    internal static async Task<(int ExitCode, string StdErr)> RunAsync(
        string fileName,
        string arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"{fileName} exceeded {timeoutSeconds}s.");
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        return (process.ExitCode, stderrTask.Result);
    }
}
