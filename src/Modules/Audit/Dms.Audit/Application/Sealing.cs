using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Audit.Application;

public sealed class AuditOptions
{
    public const string SectionName = "Dms:Audit";

    /// <summary>
    /// Base64, at least 32 bytes, from the environment or a secret store. Without it seals are
    /// plain SHA-256: they still catch edits, but someone who can write to the database could
    /// recompute a matching chain.
    /// </summary>
    public string? SealKey { get; set; }

    /// <summary>Keys that sealed older periods, kept so those seals can still be verified after a rotation.</summary>
    public string[] PreviousSealKeys { get; set; } = [];

    /// <summary>How much of the log one seal covers.</summary>
    public TimeSpan SealPeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// A period is sealed only this long after it ends, so transactions that wrote audit rows
    /// stamped inside it have committed.
    /// </summary>
    public TimeSpan SealGrace { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How far back the daily verification job re-checks.</summary>
    public int VerifyDays { get; set; } = 7;

    /// <summary>The widest range one export may cover.</summary>
    public int MaxExportDays { get; set; } = 366;

    public static bool HasValidKeys(AuditOptions options) =>
        Domain.AuditSealKey.IsValidSetting(options.SealKey) && options.PreviousSealKeys.All(Domain.AuditSealKey.IsValidSetting);
}

public enum SealProblemKind
{
    /// <summary>A sealed period's rows no longer match its digest or count: edited, deleted or back-dated.</summary>
    RowsChanged,

    /// <summary>A seal does not follow the one before it: a gap, an overlap or a wrong previous hash.</summary>
    ChainBroken,

    /// <summary>The seal's own hash is wrong: the seal was rewritten.</summary>
    SealInvalid,

    /// <summary>The seal was made with a key this installation no longer has.</summary>
    UnknownKey,

    /// <summary>Rows dated before the first seal: back-dated inserts.</summary>
    RowsBeforeFirstSeal,
}

public sealed record SealProblemDto(
    long? Sequence,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    SealProblemKind Kind,
    string Detail);

/// <param name="Proof">
/// An HMAC over the outcome and <paramref name="CheckedAt"/> with the sealing key, stored with
/// the audit record of the check. The runtime role can insert audit rows, so without it a forged
/// "intact" record could hide a failed check from the status; null when there is no key.
/// </param>
public sealed record SealVerificationDto(
    bool Intact,
    int SealsChecked,
    long RowsChecked,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<SealProblemDto> Problems,
    DateTimeOffset CheckedAt,
    string? Proof);

public sealed record SealStatusDto(
    int SealCount,
    DateTimeOffset? FirstSealedFrom,
    DateTimeOffset? SealedUntil,
    string? Algorithm,
    bool Keyed,
    string? KeyId,
    DateTimeOffset? LastVerifiedAt,
    bool? LastVerificationIntact);

/// <summary>Seals the log and checks the seals. Implemented in Infrastructure, where the rows are streamed.</summary>
public interface IAuditSealing
{
    /// <summary>Seals every complete period not sealed yet. Returns how many seals were added.</summary>
    Task<int> SealAsync(CancellationToken cancellationToken);

    /// <summary>Re-checks the seals whose periods overlap the range (all of them when both ends are null).</summary>
    Task<SealVerificationDto> VerifyAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);

    Task<SealStatusDto> GetStatusAsync(CancellationToken cancellationToken);
}

public sealed record GetSealStatusQuery : IQuery<Result<SealStatusDto>>;

public sealed record VerifySealsCommand(DateTimeOffset? From, DateTimeOffset? To) : ICommand<Result<SealVerificationDto>>;

public sealed class GetSealStatusHandler(IDmsAuthorizer authorizer, IAuditSealing sealing)
    : IQueryHandler<GetSealStatusQuery, Result<SealStatusDto>>
{
    public async Task<Result<SealStatusDto>> HandleAsync(GetSealStatusQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AuditView, cancellationToken);
        return decision.Allowed
            ? Result.Success(await sealing.GetStatusAsync(cancellationToken))
            : Result.Failure<SealStatusDto>(Error.Forbidden("auth.forbidden", decision.Explanation));
    }
}

/// <summary>An on-demand check of the chain. Its outcome is itself audited, which is what the status reports.</summary>
public sealed class VerifySealsHandler(IDmsAuthorizer authorizer, IAuditSealing sealing, IAuditWriter audit)
    : ICommandHandler<VerifySealsCommand, Result<SealVerificationDto>>
{
    public async Task<Result<SealVerificationDto>> HandleAsync(VerifySealsCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AuditView, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<SealVerificationDto>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var result = await sealing.VerifyAsync(command.From, command.To, cancellationToken);
        await audit.WriteAsync(SealAudit.Verified(result, AuditActorType.User), cancellationToken);
        return Result.Success(result);
    }
}

public static class SealAudit
{
    public static AuditRecord Verified(SealVerificationDto result, AuditActorType actor) => new()
    {
        Action = AuditActions.AuditSealsVerified,
        Outcome = result.Intact ? AuditOutcome.Success : AuditOutcome.Failed,
        ActorType = actor,
        EntityType = "AuditLog",
        Metadata = new Dictionary<string, object?>
        {
            ["checkedAt"] = result.CheckedAt.UtcTicks,
            ["proof"] = result.Proof,
            ["from"] = result.From,
            ["to"] = result.To,
            ["seals"] = result.SealsChecked,
            ["rows"] = result.RowsChecked,
            ["problems"] = result.Problems.Take(20).Select(problem => new
            {
                problem.Sequence,
                problem.PeriodStart,
                problem.PeriodEnd,
                Kind = problem.Kind.ToString(),
                problem.Detail,
            }).ToList(),
        },
    };
}
