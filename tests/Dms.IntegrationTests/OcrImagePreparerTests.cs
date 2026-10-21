using System.Buffers.Binary;
using Dms.Search.Infrastructure.Extraction;
using Shouldly;
using SkiaSharp;

namespace Dms.IntegrationTests;

/// <summary>Image clean-up before OCR (no Tesseract needed).</summary>
public sealed class OcrImagePreparerTests
{
    [Fact]
    public void A_phone_photo_is_turned_upright_from_its_EXIF_orientation()
    {
        // Stored landscape with a dark block top-left; EXIF 6 says "rotate 90° clockwise to view".
        var jpeg = WithExifOrientation(Jpeg(400, 200, darkTopLeft: true), orientation: 6);

        var png = OcrImagePreparer.Prepare(jpeg).ShouldNotBeNull();

        using var bitmap = SKBitmap.Decode(png);
        bitmap.Height.ShouldBeGreaterThan(bitmap.Width);
        // After the rotation the block sits top-right.
        Luma(bitmap.GetPixel(bitmap.Width - 10, 10)).ShouldBeLessThan(64);
        Luma(bitmap.GetPixel(10, 10)).ShouldBeGreaterThan(192);
    }

    [Fact]
    public void A_small_image_is_scaled_up_greyed_and_stamped_300_dpi()
    {
        var png = OcrImagePreparer.Prepare(Jpeg(600, 300, darkTopLeft: false, colour: SKColors.Red)).ShouldNotBeNull();

        using var bitmap = SKBitmap.Decode(png);
        Math.Max(bitmap.Width, bitmap.Height).ShouldBe(1800); // capped at 3x
        var pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        pixel.Red.ShouldBe(pixel.Green);
        pixel.Green.ShouldBe(pixel.Blue);

        // pHYs right after IHDR: 11811 pixels per metre = 300 DPI.
        System.Text.Encoding.ASCII.GetString(png, 37, 4).ShouldBe("pHYs");
        BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(41, 4)).ShouldBe(11811u);
    }

    [Fact]
    public void Bytes_that_are_not_an_image_are_left_alone()
    {
        OcrImagePreparer.Prepare("not an image"u8).ShouldBeNull();
    }

    private static int Luma(SKColor color) => (color.Red + color.Green + color.Blue) / 3;

    private static byte[] Jpeg(int width, int height, bool darkTopLeft, SKColor? colour = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(colour ?? SKColors.White);
            if (darkTopLeft)
            {
                using var paint = new SKPaint { Color = SKColors.Black };
                canvas.DrawRect(0, 0, 60, 60, paint);
            }
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    /// <summary>Inserts a minimal APP1 Exif segment holding only the Orientation tag.</summary>
    private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] tiff =
        [
            (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, // big-endian header, IFD at 8
            0, 1, // one entry
            0x01, 0x12, 0, 3, 0, 0, 0, 1, (byte)(orientation >> 8), (byte)orientation, 0, 0, // Orientation SHORT
            0, 0, 0, 0, // no next IFD
        ];
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
        return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
    }
}
