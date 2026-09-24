using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Dms.Storage.Application;
using Microsoft.Extensions.Options;
using PDFtoImage;
using SkiaSharp;

namespace Dms.Storage.Infrastructure.Processing;

internal static class PageImages
{
    /// <summary>WebP at quality 80: a text page is typically 60–150 KB at 1240 px.</summary>
    public static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
        return data.ToArray();
    }

    /// <summary>
    /// PDFium needs to seek, and uploads arrive as forward-only streams, so the PDF goes to a temp
    /// file first; it is deleted as soon as the pages are out.
    /// </summary>
    public static async IAsyncEnumerable<RenderedPage> FromPdfAsync(
        Stream content,
        RenderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dms-render-{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var file = File.Create(path))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            await using var pdf = File.OpenRead(path);
            var count = Conversion.GetPageCount(pdf, leaveOpen: true);
            for (var index = 0; index < Math.Min(count, request.MaxPages); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pdf.Position = 0;
                using var bitmap = Conversion.ToImage(
                    pdf,
                    leaveOpen: true,
                    page: index,
                    options: new RenderOptions(
                        Width: request.Width,
                        WithAspectRatio: true,
                        WithAnnotations: true,
                        BackgroundColor: SKColors.White));

                yield return new RenderedPage(index + 1, Encode(bitmap));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>PDF pages through PDFium (PDFtoImage, MIT).</summary>
public sealed class PdfPageRenderer : IPageRenderer
{
    public IAsyncEnumerable<RenderedPage>? Render(string mimeType, Stream content, RenderRequest request, CancellationToken cancellationToken) =>
        mimeType == "application/pdf" ? PageImages.FromPdfAsync(content, request, cancellationToken) : null;
}

/// <summary>
/// Photos and scans: decoded, scaled down and re-encoded, which also strips EXIF metadata such as
/// GPS positions from what viewers receive. The original stays available through download only.
/// </summary>
public sealed class ImagePageRenderer : IPageRenderer
{
    private static readonly HashSet<string> Supported = ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp"];

    public IAsyncEnumerable<RenderedPage>? Render(string mimeType, Stream content, RenderRequest request, CancellationToken cancellationToken) =>
        Supported.Contains(mimeType) ? RenderAsync(content, request, cancellationToken) : null;

    private static async IAsyncEnumerable<RenderedPage> RenderAsync(
        Stream content,
        RenderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var original = SKBitmap.Decode(buffer)
            ?? throw new InvalidOperationException("The image could not be decoded.");

        if (original.Width <= request.Width)
        {
            yield return new RenderedPage(1, PageImages.Encode(original));
            yield break;
        }

        var height = (int)Math.Round(original.Height * (request.Width / (double)original.Width));
        using var scaled = original.Resize(new SKImageInfo(request.Width, Math.Max(height, 1)), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("The image could not be scaled.");

        yield return new RenderedPage(1, PageImages.Encode(scaled));
    }
}

/// <summary>
/// Office documents through Gotenberg's LibreOffice route to PDF, then like any PDF. Off unless a
/// Gotenberg URL is configured; without it Office files are download-only.
/// </summary>
public sealed class OfficePageRenderer(HttpClient http, IOptions<StorageOptions> options) : IPageRenderer
{
    /// <summary>LibreOffice picks the import filter from the extension, so the type maps back to one.</summary>
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.Ordinal)
    {
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "docx",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "xlsx",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "pptx",
        ["application/vnd.oasis.opendocument.text"] = "odt",
        ["application/vnd.oasis.opendocument.spreadsheet"] = "ods",
        ["application/vnd.oasis.opendocument.presentation"] = "odp",
        ["application/msword"] = "doc",
        ["application/vnd.ms-excel"] = "xls",
        ["application/vnd.ms-powerpoint"] = "ppt",
        ["application/x-ole-storage"] = "doc",
        ["application/rtf"] = "rtf",
        ["text/plain"] = "txt",
        ["text/csv"] = "csv",
    };

    public IAsyncEnumerable<RenderedPage>? Render(string mimeType, Stream content, RenderRequest request, CancellationToken cancellationToken)
    {
        var url = options.Value.Renditions.GotenbergUrl;
        if (string.IsNullOrWhiteSpace(url) || !Extensions.TryGetValue(mimeType, out var extension))
        {
            return null;
        }

        var name = request.FileName is { } fileName && Path.GetExtension(fileName).Length > 1
            ? fileName
            : $"document.{extension}";

        return ConvertAsync(new Uri(new Uri(url), "forms/libreoffice/convert"), name, content, request, cancellationToken);
    }

    private async IAsyncEnumerable<RenderedPage> ConvertAsync(
        Uri endpoint,
        string fileName,
        Stream content,
        RenderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "files", fileName);

        using var response = await http.PostAsync(endpoint, form, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var pdf = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var page in PageImages.FromPdfAsync(pdf, request, cancellationToken))
        {
            yield return page;
        }
    }
}

/// <summary>
/// Stamps the viewer and the time diagonally across a page, repeated, at low opacity: readable
/// enough to identify a leaked photo of the screen, faint enough to read through.
/// </summary>
public sealed class SkiaWatermarker : IWatermarker
{
    private static readonly SKTypeface Typeface =
        SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;

    public byte[] Apply(byte[] image, string text)
    {
        using var bitmap = SKBitmap.Decode(image) ?? throw new InvalidOperationException("Not an image.");
        using var canvas = new SKCanvas(bitmap);
        using var font = new SKFont(Typeface, Math.Max(bitmap.Width / 32f, 12f));
        using var paint = new SKPaint { Color = new SKColor(90, 90, 90, 56), IsAntialias = true };

        var step = font.Size * 9;
        canvas.RotateDegrees(-30, bitmap.Width / 2f, bitmap.Height / 2f);
        for (var y = -bitmap.Height; y < bitmap.Height * 2; y += (int)step)
        {
            for (var x = -bitmap.Width; x < bitmap.Width * 2; x += (int)(step * 2.5))
            {
                canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
            }
        }

        canvas.Flush();
        return PageImages.Encode(bitmap);
    }
}
