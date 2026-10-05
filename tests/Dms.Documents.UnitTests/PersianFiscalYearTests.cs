using Dms.Documents.Domain;
using Shouldly;

namespace Dms.Documents.UnitTests;

public sealed class PersianFiscalYearTests
{
    [Fact]
    public void Farvardin_starts_at_midnight_in_Tehran()
    {
        // 2026-03-21 00:00 in Tehran (UTC+3:30) is the first moment of 1405.
        PersianFiscalYear.Of(new DateTimeOffset(2026, 3, 20, 20, 30, 0, TimeSpan.Zero)).ShouldBe(1405);
        // Half an hour earlier is still the evening of 1404-12-29.
        PersianFiscalYear.Of(new DateTimeOffset(2026, 3, 20, 19, 0, 0, TimeSpan.Zero)).ShouldBe(1404);
    }

    [Fact]
    public void Only_the_current_jalali_year_is_open()
    {
        var during1405 = new DateTimeOffset(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);

        PersianFiscalYear.IsOpen(1405, during1405).ShouldBeTrue();
        PersianFiscalYear.IsOpen(1404, during1405).ShouldBeFalse();
    }
}
