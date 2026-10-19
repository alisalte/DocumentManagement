using Dms.Search.Infrastructure.Extraction;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// How tesseract's word-level TSV becomes text. No tesseract needed: the TSV is written by hand,
/// in the column layout tesseract 5 prints.
/// </summary>
public sealed class OcrTextTests
{
    private const string Header = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext";

    private static string Word(int block, int line, int height, double confidence, string text) =>
        $"5\t1\t{block}\t1\t{line}\t1\t0\t0\t10\t{height}\t{confidence}\t{text}";

    private static string Tsv(params string[] words) => string.Join('\n', [Header, .. words]);

    [Fact]
    public void Low_confidence_words_and_letterless_lines_are_dropped()
    {
        var pass = OcrText.Parse(
            Tsv(
                Word(1, 1, 30, 91, "درخواست"),
                Word(1, 1, 30, 12, "pros"),
                Word(1, 1, 30, 88, "تعمیر"),
                Word(1, 2, 30, 80, "۴"),
                Word(1, 2, 30, 80, "۲۹"),
                Word(2, 1, 32, 95, "pargarnet.com")),
            minConfidence: 30);

        OcrText.Join(pass.Lines).ShouldBe("درخواست تعمیر\n\npargarnet.com");
        pass.MedianWordHeight.ShouldBe(30);
    }

    [Fact]
    public void Only_sparse_lines_with_mostly_new_words_are_added()
    {
        var layout = OcrText.Parse(Tsv(Word(1, 1, 30, 90, "مسئول"), Word(1, 1, 30, 90, "هر"), Word(1, 1, 30, 90, "کار.")), 30).Lines;
        var sparse = OcrText.Parse(
            Tsv(
                Word(1, 1, 30, 90, "مسئول"),
                Word(1, 1, 30, 90, "هر"),
                Word(1, 1, 30, 90, "کار"),
                Word(2, 1, 30, 90, "تعمیر"),
                Word(2, 1, 30, 90, "الکتروموتور")),
            75).Lines;

        OcrText.Join(OcrText.Merge(layout, sparse)).ShouldBe("مسئول هر کار.\n\nتعمیر الکتروموتور");
    }
}
