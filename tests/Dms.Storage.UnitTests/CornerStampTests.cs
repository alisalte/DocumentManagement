using Dms.Storage.Infrastructure.Processing;
using Shouldly;
using SkiaSharp;

namespace Dms.Storage.UnitTests;

/// <summary>The indicator number in the page corner (FR-11): drawn top left, leaving the rest alone.</summary>
public sealed class CornerStampTests
{
    private static byte[] WhitePage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static int DarkPixels(SKBitmap bitmap, SKRectI area)
    {
        var dark = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 128 && pixel.Green < 128 && pixel.Blue < 128)
                {
                    dark++;
                }
            }
        }

        return dark;
    }

    [Fact]
    public void The_number_is_drawn_in_the_top_left_corner_only()
    {
        var stamped = new SkiaWatermarker().StampCorner(WhitePage(1240, 1754), "CTR-1405-00045");

        using var bitmap = SKBitmap.Decode(stamped);
        bitmap.Width.ShouldBe(1240);
        DarkPixels(bitmap, new SKRectI(0, 0, 620, 120)).ShouldBeGreaterThan(50, "the number is written top left");
        DarkPixels(bitmap, new SKRectI(620, 200, 1240, 1754)).ShouldBe(0, "the rest of the page is untouched");
    }

    [Fact]
    public void Blank_text_leaves_the_page_as_it_was()
    {
        var page = WhitePage(200, 200);

        new SkiaWatermarker().StampCorner(page, "   ").ShouldBe(page);
    }
}
