using System.Diagnostics;
using Dms.Application;
using Dms.SharedKernel;

namespace Dms.Migrator;

/// <summary>The migrator acts as the system, not as a user.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public UserId? UserId => null;

    public string? IpAddress => null;

    public string? UserAgent => "migrator";

    public string CorrelationId => Activity.Current?.TraceId.ToString() ?? "migrator";
}
