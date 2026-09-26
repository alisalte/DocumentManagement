using System.Diagnostics;
using System.Text;
using Dms.Search.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Search.Infrastructure.Extraction;

/// <summary>
/// Shells out to the system <c>tesseract</c> binary with <c>fas+eng</c> (section 8.1, risk R2).
/// Keeps OCR out of process so a hung page cannot take down the worker.
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

            var outputBase = Path.Combine(work.FullName, "out");
            var args = $"\"{input}\" \"{outputBase}\" -l {_options.OcrLanguages} --psm 3";
            var (exit, stderr) = await RunAsync(
                _options.Tesseract.Command,
                args,
                _options.Tesseract.TimeoutSeconds,
                cancellationToken);

            var textPath = outputBase + ".txt";
            if (exit != 0 || !File.Exists(textPath))
            {
                logger.LogWarning("tesseract failed (exit {Exit}): {Error}", exit, stderr.Trim());
                return string.Empty;
            }

            var text = await File.ReadAllTextAsync(textPath, Encoding.UTF8, cancellationToken);
            return text.Trim();
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
