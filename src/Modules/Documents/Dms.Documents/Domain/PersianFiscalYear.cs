using System.Globalization;

namespace Dms.Documents.Domain;

/// <summary>
/// The Iranian fiscal year is the Jalali year in Tehran. The number (1404, 1405) is the
/// business identity of the period; timestamps themselves stay UTC.
/// </summary>
public static class PersianFiscalYear
{
    private static readonly PersianCalendar Calendar = new();

    public static int Of(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Tehran);
        return Calendar.GetYear(local.DateTime);
    }

    /// <summary>A year before the current one is closed: reporting only, no filing or edits.</summary>
    public static bool IsOpen(int year, DateTimeOffset now) => year == Of(now);

    private static TimeZoneInfo Tehran { get; } = LoadTehran();

    private static TimeZoneInfo LoadTehran()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
    }
}
