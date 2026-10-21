using System.Buffers.Binary;
using SkiaSharp;

namespace Dms.Search.Infrastructure.Extraction;

/// <summary>
/// Normalises a photo or scan before Tesseract sees it. Tesseract ignores EXIF orientation (a
/// phone photo taken upright reaches it sideways and yields nothing), assumes 70 DPI when the file
/// carries none, and reads small text badly. So: apply the EXIF rotation, drop colour, scale text
/// to a size the LSTM models were trained on, and stamp 300 DPI into the PNG.
/// </summary>
public static class OcrImagePreparer
{
    /// <summary>Images whose long side is below this are scaled up (screenshots, thumbnails).</summary>
    private const int MinLongSide = 2400;

    /// <summary>Above this, scaled down: more pixels only cost time once text is this large.</summary>
    private const int MaxLongSide = 4800;

    /// <summary>Upscaling beyond this only magnifies blur.</summary>
    private const float MaxUpscale = 3f;

    private const int Dpi = 300;

    /// <summary>
    /// Reads <paramref name="image"/> and returns the prepared PNG, or the original bytes and type
    /// when it cannot be decoded.
    /// </summary>
    public static async Task<(Stream Image, string MimeType)> PrepareAsync(
        Stream image,
        string mimeType,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);
        var original = buffer.ToArray();

        byte[]? prepared;
        try
        {
            prepared = Prepare(original);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            prepared = null;
        }

        return prepared is null
            ? (new MemoryStream(original, writable: false), mimeType)
            : (new MemoryStream(prepared, writable: false), "image/png");
    }

    /// <summary>
    /// The prepared image as PNG, or null when the bytes cannot be decoded (Tesseract then gets the
    /// original, which may still work for formats Skia does not read, such as TIFF).
    /// </summary>
    public static byte[]? Prepare(ReadOnlySpan<byte> encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        using var upright = Orient(decoded, codec.EncodedOrigin);
        var longSide = Math.Max(upright.Width, upright.Height);
        var scale = longSide < MinLongSide
            ? Math.Min(MaxUpscale, (float)MinLongSide / longSide)
            : longSide > MaxLongSide ? (float)MaxLongSide / longSide : 1f;

        var width = Math.Max(1, (int)Math.Round(upright.Width * scale));
        var height = Math.Max(1, (int)Math.Round(upright.Height * scale));

        // Opaque white underneath: transparent PNG text is otherwise black on black.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(upright);
        using var grey = SKColorFilter.CreateColorMatrix(
        [
            0.299f, 0.587f, 0.114f, 0, 0,
            0.299f, 0.587f, 0.114f, 0, 0,
            0.299f, 0.587f, 0.114f, 0, 0,
            0, 0, 0, 1, 0,
        ]);
        using var paint = new SKPaint { ColorFilter = grey };
        canvas.DrawImage(
            image,
            new SKRect(0, 0, width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            paint);
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png is null ? null : WithDpi(png.ToArray(), Dpi);
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return source.Copy();
        }

        var swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swaps ? source.Height : source.Width;
        var height = swaps ? source.Width : source.Height;

        var result = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(result);
        canvas.SetMatrix(OriginMatrix(origin, source.Width, source.Height));
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return result;
    }

    /// <summary>Maps the stored pixels onto the upright image (EXIF orientation 2–8).</summary>
    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) =>
        origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

    /// <summary>Inserts a pHYs chunk right after IHDR so Tesseract reads the resolution.</summary>
    internal static byte[] WithDpi(byte[] png, int dpi)
    {
        // Signature (8) + IHDR length/type/data/crc (4 + 4 + 13 + 4).
        const int afterIhdr = 8 + 25;
        if (png.Length < afterIhdr)
        {
            return png;
        }

        var pixelsPerMetre = (uint)Math.Round(dpi / 0.0254);
        var chunk = new byte[4 + 4 + 9 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0, 4), 9);
        "pHYs"u8.CopyTo(chunk.AsSpan(4, 4));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8, 4), pixelsPerMetre);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(12, 4), pixelsPerMetre);
        chunk[16] = 1; // unit: metre
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(17, 4), Crc32(chunk.AsSpan(4, 13)));

        var result = new byte[png.Length + chunk.Length];
        png.AsSpan(0, afterIhdr).CopyTo(result);
        chunk.CopyTo(result.AsSpan(afterIhdr));
        png.AsSpan(afterIhdr).CopyTo(result.AsSpan(afterIhdr + chunk.Length));
        return result;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
