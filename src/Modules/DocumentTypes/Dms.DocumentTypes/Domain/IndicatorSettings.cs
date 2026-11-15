using Dms.DocumentTypes.Contracts;
using Dms.SharedKernel;

namespace Dms.DocumentTypes.Domain;

/// <summary>
/// The single row of global indicator number settings. Until an administrator saves it there is
/// no row and <see cref="IndicatorPattern.Default"/> applies.
/// </summary>
public sealed class IndicatorSettings
{
    public const short SingletonId = 1;

    private IndicatorSettings()
    {
    }

    public short Id { get; private set; } = SingletonId;

    /// <summary>Used by every document type without a pattern of its own; always contains {TYPE}.</summary>
    public string DefaultPattern { get; private set; } = IndicatorPattern.Default;

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId? UpdatedBy { get; private set; }

    public static IndicatorSettings CreateDefault(DateTimeOffset now) => new() { UpdatedAt = now };

    /// <param name="pattern">Already parsed; the caller checks it has {TYPE} and does not collide.</param>
    public void SetDefaultPattern(IndicatorPattern pattern, UserId by, DateTimeOffset now)
    {
        DefaultPattern = pattern.Text;
        UpdatedBy = by;
        UpdatedAt = now;
    }
}
