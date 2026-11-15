using System.Globalization;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.DocumentTypes.Domain;
using Dms.SharedKernel;

namespace Dms.DocumentTypes.Application;

public sealed record IndicatorSettingsDto(string DefaultPattern, DateTimeOffset? UpdatedAt);

public sealed record GetIndicatorSettingsQuery : IQuery<Result<IndicatorSettingsDto>>;

public sealed record UpdateIndicatorSettingsCommand(string DefaultPattern) : ICommand<Result>;

/// <param name="DocumentTypeId">The type whose code fills {TYPE} in the sample; a placeholder code when null.</param>
public sealed record PreviewIndicatorPatternQuery(string Pattern, Guid? DocumentTypeId)
    : IQuery<Result<IndicatorPatternPreviewDto>>;

public sealed record IndicatorPatternPreviewDto(string Pattern, string Sample);

/// <summary>
/// Keeps indicator numbers unique across document types (BR-16). Each type has its own counter,
/// so two types whose patterns read the same once {TYPE} is filled in would issue the same numbers.
/// The unique index on the number is the last line of defence; this catches it when saving.
/// </summary>
public sealed class IndicatorPatternPolicy(IDocumentTypeRepository repository)
{
    /// <param name="defaultPattern">The default pattern after the change being checked.</param>
    /// <param name="changedType">The type whose own pattern is being changed, with its new pattern (null = default).</param>
    public async Task<Result> CheckAsync(
        string defaultPattern,
        (DocumentTypeId Id, string? Pattern)? changedType,
        CancellationToken cancellationToken)
    {
        var types = await repository.ListAsync(includeInactive: true, cancellationToken);
        var seen = new Dictionary<string, DocumentType>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var own = changedType is { } changed && changed.Id == type.Id ? changed.Pattern : type.Settings.IndicatorPattern;
            var parsed = IndicatorPattern.Parse(own ?? defaultPattern);
            if (parsed.IsFailure)
            {
                // A stored pattern that no longer parses cannot issue numbers anyway.
                continue;
            }

            var resolved = parsed.Value!.Resolve(type.Code);
            if (seen.TryGetValue(resolved, out var other))
            {
                return Result.Failure(Error.Conflict(
                    "indicator_pattern.collides",
                    $"Document types «{other.Name}» and «{type.Name}» would get the same indicator numbers. Give one of them a different prefix or use {{TYPE}}."));
            }

            seen.Add(resolved, type);
        }

        return Result.Success();
    }

    public async Task<string> CurrentDefaultAsync(CancellationToken cancellationToken) =>
        (await repository.FindIndicatorSettingsAsync(cancellationToken))?.DefaultPattern ?? IndicatorPattern.Default;

    /// <summary>A type's own pattern, normalized: null for blank (use the default), canonical text otherwise.</summary>
    public static Result<string?> Normalize(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Result.Success<string?>(null);
        }

        var parsed = IndicatorPattern.Parse(pattern);
        return parsed.IsFailure ? Result.Failure<string?>(parsed.Error) : Result.Success<string?>(parsed.Value!.Text);
    }
}

public sealed class GetIndicatorSettingsHandler(IDmsAuthorizer authorizer, IDocumentTypeRepository repository)
    : IQueryHandler<GetIndicatorSettingsQuery, Result<IndicatorSettingsDto>>
{
    public async Task<Result<IndicatorSettingsDto>> HandleAsync(GetIndicatorSettingsQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IndicatorSettingsDto>(DocumentTypeErrors.Forbidden(decision.Explanation));
        }

        var settings = await repository.FindIndicatorSettingsAsync(cancellationToken);
        return Result.Success(new IndicatorSettingsDto(settings?.DefaultPattern ?? IndicatorPattern.Default, settings?.UpdatedAt));
    }
}

public sealed class UpdateIndicatorSettingsHandler(
    IDmsAuthorizer authorizer,
    IDocumentTypeRepository repository,
    IndicatorPatternPolicy policy,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<UpdateIndicatorSettingsCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateIndicatorSettingsCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed || currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentTypeErrors.Forbidden(decision.Explanation));
        }

        var parsed = IndicatorPattern.Parse(command.DefaultPattern);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error);
        }

        var pattern = parsed.Value!;
        if (!pattern.HasTypeCode)
        {
            return Result.Failure(Error.Validation(
                "indicator_pattern.default_needs_type",
                "The default pattern is shared by several document types, so it needs {TYPE}."));
        }

        var collision = await policy.CheckAsync(pattern.Text, null, cancellationToken);
        if (collision.IsFailure)
        {
            return collision;
        }

        var now = timeProvider.GetUtcNow();
        var settings = await repository.FindIndicatorSettingsAsync(cancellationToken);
        if (settings is null)
        {
            settings = IndicatorSettings.CreateDefault(now);
            repository.AddIndicatorSettings(settings);
        }

        var before = settings.DefaultPattern;
        if (before == pattern.Text)
        {
            return Result.Success();
        }

        settings.SetDefaultPattern(pattern, actor, now);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.IndicatorDefaultPatternChanged,
                EntityType = "IndicatorSettings",
                Metadata = new Dictionary<string, object?> { ["before"] = before, ["after"] = pattern.Text },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class PreviewIndicatorPatternHandler(
    IDmsAuthorizer authorizer,
    IDocumentTypeRepository repository,
    TimeProvider timeProvider) : IQueryHandler<PreviewIndicatorPatternQuery, Result<IndicatorPatternPreviewDto>>
{
    public const string SampleTypeCode = "TYPE";
    public const string SampleCategoryCode = "CAT";

    public async Task<Result<IndicatorPatternPreviewDto>> HandleAsync(
        PreviewIndicatorPatternQuery query,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IndicatorPatternPreviewDto>(DocumentTypeErrors.Forbidden(decision.Explanation));
        }

        var parsed = IndicatorPattern.Parse(query.Pattern);
        if (parsed.IsFailure)
        {
            return Result.Failure<IndicatorPatternPreviewDto>(parsed.Error);
        }

        var typeCode = SampleTypeCode;
        if (query.DocumentTypeId is { } typeId)
        {
            typeCode = (await repository.FindAsync(new DocumentTypeId(typeId), cancellationToken))?.Code ?? SampleTypeCode;
        }

        // The Persian calendar year: a sample only, the real number uses the document's fiscal year.
        var year = new PersianCalendar().GetYear(timeProvider.GetUtcNow().UtcDateTime);
        var pattern = parsed.Value!;
        return Result.Success(new IndicatorPatternPreviewDto(pattern.Text, pattern.Format(typeCode, year, SampleCategoryCode, 1)));
    }
}
