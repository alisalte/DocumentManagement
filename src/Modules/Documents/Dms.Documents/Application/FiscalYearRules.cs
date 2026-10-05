using Dms.Documents.Domain;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

internal static class FiscalYearRules
{
    /// <summary>Null when the document's year is the open year. Otherwise the year is report-only.</summary>
    public static Error? RejectClosed(int documentYear, TimeProvider time) =>
        PersianFiscalYear.IsOpen(documentYear, time.GetUtcNow()) ? null : DocumentErrors.FiscalYearClosed;
}
