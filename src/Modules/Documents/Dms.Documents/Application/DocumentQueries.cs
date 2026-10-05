using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Dms.Storage.Contracts;

namespace Dms.Documents.Application;

/// <summary>Direct document counts per category, for folder cards. The UI adds descendant folders.</summary>
public sealed record ListCategoryDocumentCountsQuery(int? FiscalYear = null)
    : IQuery<Result<IReadOnlyList<CategoryDocumentCountDto>>>;

public sealed record ListDocumentsQuery(
    Guid? CategoryId,
    bool IncludeSubcategories,
    string? Search,
    Guid? TagId,
    int? Page,
    int? PageSize,
    int? FiscalYear = null) : IQuery<Result<PagedResult<DocumentListItemDto>>>;

/// <summary>One Jalali year the caller can switch to. Open is the current Tehran year; earlier years are report-only.</summary>
public sealed record FiscalYearOptionDto(int Year, string Status, int DocumentCount);

public sealed record FiscalYearOverviewDto(int CurrentYear, IReadOnlyList<FiscalYearOptionDto> Years);

public sealed record ListFiscalYearsQuery : IQuery<Result<FiscalYearOverviewDto>>;

public sealed record GetDocumentQuery(Guid Id) : IQuery<Result<DocumentDetailsDto>>;

public sealed record ListVersionsQuery(Guid DocumentId) : IQuery<Result<IReadOnlyList<DocumentVersionDto>>>;

/// <summary>One version on its own, for callers who may see that version but not the whole document.</summary>
public sealed record GetVersionQuery(Guid DocumentId, Guid VersionId) : IQuery<Result<VersionDetailsDto>>;

/// <summary>
/// A version with just enough of its document to make sense: no category, no history, no other
/// versions. This is what a share recipient sees (decision D8).
/// </summary>
public sealed record VersionDetailsDto(
    Guid DocumentId,
    string Title,
    string? Description,
    Guid DocumentTypeId,
    DocumentVersionDto Version,
    IReadOnlyList<string> AllowedActions);

public sealed record ListRecycleBinQuery(int? Page, int? PageSize) : IQuery<Result<PagedResult<DocumentListItemDto>>>;

public sealed record ListTagsQuery(string? Search) : IQuery<Result<IReadOnlyList<TagDto>>>;

/// <summary>A category as the caller sees it: whether they may browse it and whether they may file into it.</summary>
public sealed record CategoryNodeDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Code,
    string? Description,
    int Depth,
    bool IsActive,
    int SortOrder,
    bool CanView,
    bool CanCreate);

public sealed record ListCategoriesQuery : IQuery<Result<IReadOnlyList<CategoryNodeDto>>>;

public sealed class ListDocumentsHandler(
    IAccessScopeProvider scopes,
    IDocumentReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<ListDocumentsQuery, Result<PagedResult<DocumentListItemDto>>>
{
    public async Task<Result<PagedResult<DocumentListItemDto>>> HandleAsync(
        ListDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } viewer)
        {
            return Result.Failure<PagedResult<DocumentListItemDto>>(DocumentErrors.Unauthenticated);
        }

        // Section 5.7: the same rules as a single decision, applied as one SQL predicate.
        var scope = await scopes.GetScopeAsync(viewer, PermissionCodes.DocumentView, cancellationToken);
        var (page, pageSize) = PagedResult<DocumentListItemDto>.Normalize(query.Page, query.PageSize);

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var result = await readModel.ListAsync(
            new DocumentListFilter(
                query.CategoryId,
                query.IncludeSubcategories,
                search,
                query.TagId,
                page,
                pageSize,
                query.FiscalYear),
            scope,
            viewer,
            cancellationToken);

        return Result.Success(result);
    }
}

public sealed class ListCategoryDocumentCountsHandler(
    IAccessScopeProvider scopes,
    IDocumentReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<ListCategoryDocumentCountsQuery, Result<IReadOnlyList<CategoryDocumentCountDto>>>
{
    public async Task<Result<IReadOnlyList<CategoryDocumentCountDto>>> HandleAsync(
        ListCategoryDocumentCountsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } viewer)
        {
            return Result.Failure<IReadOnlyList<CategoryDocumentCountDto>>(DocumentErrors.Unauthenticated);
        }

        var scope = await scopes.GetScopeAsync(viewer, PermissionCodes.DocumentView, cancellationToken);
        var counts = await readModel.CountByCategoryAsync(scope, viewer, query.FiscalYear, cancellationToken);
        return Result.Success(counts);
    }
}

public sealed class ListFiscalYearsHandler(
    IAccessScopeProvider scopes,
    IDocumentReadModel readModel,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IQueryHandler<ListFiscalYearsQuery, Result<FiscalYearOverviewDto>>
{
    public async Task<Result<FiscalYearOverviewDto>> HandleAsync(
        ListFiscalYearsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } viewer)
        {
            return Result.Failure<FiscalYearOverviewDto>(DocumentErrors.Unauthenticated);
        }

        var current = PersianFiscalYear.Of(timeProvider.GetUtcNow());
        var scope = await scopes.GetScopeAsync(viewer, PermissionCodes.DocumentView, cancellationToken);
        var counts = await readModel.CountByFiscalYearAsync(scope, viewer, cancellationToken);

        var years = counts
            .Where(row => row.Year > 0)
            .Select(row => row.Year)
            .Append(current)
            .Distinct()
            .OrderByDescending(year => year)
            .Select(year => new FiscalYearOptionDto(
                year,
                year == current ? "Open" : "Closed",
                counts.FirstOrDefault(row => row.Year == year)?.Count ?? 0))
            .ToList();

        return Result.Success(new FiscalYearOverviewDto(current, years));
    }
}

public sealed class GetDocumentHandler(
    DocumentAccess access,
    IDocumentReadModel readModel,
    IDocumentTypeCatalog documentTypes,
    IRecordReadModel records,
    TimeProvider timeProvider)
    : IQueryHandler<GetDocumentQuery, Result<DocumentDetailsDto>>
{
    /// <summary>Actions the UI may offer. Checked one by one with the real evaluator.</summary>
    private static readonly string[] ActionPermissions =
    [
        PermissionCodes.DocumentDownload,
        PermissionCodes.DocumentPrint,
        PermissionCodes.DocumentEdit,
        PermissionCodes.DocumentCreateVersion,
        PermissionCodes.DocumentDelete,
        PermissionCodes.DocumentManagePermission,
        PermissionCodes.DocumentShare,
        PermissionCodes.DocumentShareExternal,
    ];

    public async Task<Result<DocumentDetailsDto>> HandleAsync(GetDocumentQuery query, CancellationToken cancellationToken)
    {
        var visible = await access.RequireAsync(query.Id, PermissionCodes.DocumentView, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<DocumentDetailsDto>(visible.Error);
        }

        var details = await readModel.GetAsync(new DocumentId(query.Id), cancellationToken);
        if (details is null)
        {
            return Result.Failure<DocumentDetailsDto>(DocumentErrors.DocumentNotFound);
        }

        var actions = new List<string> { PermissionCodes.DocumentView };
        foreach (var permission in ActionPermissions)
        {
            if (await access.IsAllowedAsync(query.Id, permission, cancellationToken))
            {
                actions.Add(permission);
            }
        }

        var fiscalYearOpen = PersianFiscalYear.IsOpen(details.FiscalYear, timeProvider.GetUtcNow());
        if (!fiscalYearOpen)
        {
            actions.RemoveAll(action => action is not (
                PermissionCodes.DocumentView
                or PermissionCodes.DocumentViewDraft
                or PermissionCodes.DocumentDownload
                or PermissionCodes.DocumentPrint));
        }

        var schema = details.CurrentSchemaVersionId is { } schemaId
            ? await documentTypes.GetSchemaAsync(new DocumentTypeVersionId(schemaId), cancellationToken)
            : null;

        var record = await records.GetByDocumentAsync(new DocumentId(query.Id), cancellationToken);

        return Result.Success(details with
        {
            AllowedActions = actions,
            FiscalYearOpen = fiscalYearOpen,
            CurrentMetadata = MetadataPresenter.ForApi(schema, details.CurrentMetadata?.GetRawText()),
            Record = record,
        });
    }
}

public sealed class ListVersionsHandler(
    DocumentAccess access,
    IDocumentReadModel readModel,
    IStorageService storage,
    IDocumentTypeCatalog documentTypes,
    ICurrentUser currentUser) : IQueryHandler<ListVersionsQuery, Result<IReadOnlyList<DocumentVersionDto>>>
{
    public async Task<Result<IReadOnlyList<DocumentVersionDto>>> HandleAsync(
        ListVersionsQuery query,
        CancellationToken cancellationToken)
    {
        var visible = await access.RequireAsync(query.DocumentId, PermissionCodes.DocumentView, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DocumentVersionDto>>(visible.Error);
        }

        var details = await readModel.GetAsync(new DocumentId(query.DocumentId), cancellationToken);
        if (details is null)
        {
            return Result.Failure<IReadOnlyList<DocumentVersionDto>>(DocumentErrors.DocumentNotFound);
        }

        var versions = await readModel.ListVersionsAsync(new DocumentId(query.DocumentId), cancellationToken);

        // Decision D6: unpublished rows only for their author or holders of VIEW_DRAFT.
        var canSeeDrafts = await access.IsAllowedAsync(
            query.DocumentId,
            PermissionCodes.DocumentViewDraft,
            cancellationToken);

        var viewer = currentUser.UserId?.Value;
        var visibleVersions = versions
            .Where(version => version.IsPublished || canSeeDrafts || version.CreatedBy == viewer)
            .ToList();

        var files = await storage.FindManyAsync(
            visibleVersions.Select(version => new StorageObjectId(version.StorageObjectId)).Distinct().ToList(),
            cancellationToken);

        // Each row is read with the schema it was written against, so a field added or retyped in
        // a later schema never changes how an old version's values are presented.
        var schemas = new Dictionary<Guid, DocumentTypeSchema?>();
        foreach (var schemaId in visibleVersions.Select(version => version.SchemaVersionId).Distinct())
        {
            schemas[schemaId] = await documentTypes.GetSchemaAsync(new DocumentTypeVersionId(schemaId), cancellationToken);
        }

        IReadOnlyList<DocumentVersionDto> result = visibleVersions
            .Select(version => version with
            {
                Metadata = MetadataPresenter.ForApi(schemas[version.SchemaVersionId], version.Metadata?.GetRawText()),
                IsCurrent = version.Id == details.CurrentVersionId,
                IsEffective = version.Id == details.EffectiveVersionId,
                ScanStatus = files.TryGetValue(new StorageObjectId(version.StorageObjectId), out var file)
                    ? file.ScanStatus.ToString()
                    : "Unknown",
            })
            .ToList();

        return Result.Success(result);
    }
}

public sealed class GetVersionHandler(
    DocumentAccess access,
    IDocumentReadModel readModel,
    IStorageService storage,
    IDocumentTypeCatalog documentTypes) : IQueryHandler<GetVersionQuery, Result<VersionDetailsDto>>
{
    public async Task<Result<VersionDetailsDto>> HandleAsync(GetVersionQuery query, CancellationToken cancellationToken)
    {
        // VIEW on this version is enough; the draft and scan gates of the version apply.
        var visible = await access.RequireAsync(query.DocumentId, PermissionCodes.DocumentView, query.VersionId, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<VersionDetailsDto>(visible.Error);
        }

        var details = await readModel.GetAsync(new DocumentId(query.DocumentId), cancellationToken);
        var version = details is null
            ? null
            : (await readModel.ListVersionsAsync(new DocumentId(details.Id), cancellationToken))
                .FirstOrDefault(item => item.Id == query.VersionId);
        if (details is null || version is null)
        {
            return Result.Failure<VersionDetailsDto>(DocumentErrors.VersionNotFound);
        }

        var file = await storage.FindAsync(new StorageObjectId(version.StorageObjectId), cancellationToken);
        var schema = await documentTypes.GetSchemaAsync(new DocumentTypeVersionId(version.SchemaVersionId), cancellationToken);

        var actions = new List<string> { PermissionCodes.DocumentView };
        foreach (var permission in new[] { PermissionCodes.DocumentDownload, PermissionCodes.DocumentPrint })
        {
            if (await access.IsVersionAllowedAsync(query.DocumentId, permission, query.VersionId, cancellationToken))
            {
                actions.Add(permission);
            }
        }

        return Result.Success(new VersionDetailsDto(
            details.Id,
            details.Title,
            details.Description,
            details.DocumentTypeId,
            version with
            {
                Metadata = MetadataPresenter.ForApi(schema, version.Metadata?.GetRawText()),
                IsCurrent = version.Id == details.CurrentVersionId,
                IsEffective = version.Id == details.EffectiveVersionId,
                ScanStatus = file?.ScanStatus.ToString() ?? "Unknown",
            },
            actions));
    }
}

public sealed class ListRecycleBinHandler(
    DocumentAccess access,
    IAccessScopeProvider scopes,
    IDocumentReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<ListRecycleBinQuery, Result<PagedResult<DocumentListItemDto>>>
{
    public async Task<Result<PagedResult<DocumentListItemDto>>> HandleAsync(
        ListRecycleBinQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } viewer)
        {
            return Result.Failure<PagedResult<DocumentListItemDto>>(DocumentErrors.Unauthenticated);
        }

        var (page, pageSize) = PagedResult<DocumentListItemDto>.Normalize(query.Page, query.PageSize);

        // Whoever may purge has to see what they would purge; everyone else sees what they may restore.
        AccessScope? scope = await access.IsSystemAllowedAsync(PermissionCodes.DocumentPurge, cancellationToken)
            ? null
            : await scopes.GetScopeAsync(viewer, PermissionCodes.DocumentRestore, cancellationToken);

        return Result.Success(await readModel.ListDeletedAsync(scope, page, pageSize, cancellationToken));
    }
}

public sealed class ListTagsHandler(IDocumentReadModel readModel, ICurrentUser currentUser)
    : IQueryHandler<ListTagsQuery, Result<IReadOnlyList<TagDto>>>
{
    public async Task<Result<IReadOnlyList<TagDto>>> HandleAsync(ListTagsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<IReadOnlyList<TagDto>>(DocumentErrors.Unauthenticated);
        }

        // Tags are shared vocabulary for filing, like document types; they carry no document data.
        return Result.Success(await readModel.ListTagsAsync(query.Search, limit: 50, cancellationToken));
    }
}

public sealed class ListCategoriesHandler(
    DocumentAccess access,
    IAccessScopeProvider scopes,
    IDocumentReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<ListCategoriesQuery, Result<IReadOnlyList<CategoryNodeDto>>>
{
    public async Task<Result<IReadOnlyList<CategoryNodeDto>>> HandleAsync(
        ListCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } viewer)
        {
            return Result.Failure<IReadOnlyList<CategoryNodeDto>>(DocumentErrors.Unauthenticated);
        }

        var all = await readModel.ListCategoriesAsync(cancellationToken);
        var view = await scopes.GetScopeAsync(viewer, PermissionCodes.DocumentView, cancellationToken);
        var create = await scopes.GetScopeAsync(viewer, PermissionCodes.DocumentCreate, cancellationToken);
        var isCategoryAdmin = await access.IsSystemAllowedAsync(PermissionCodes.AdminManageCategories, cancellationToken);

        bool CanView(Guid id) => view.AllowedCategories.Contains(id) && !view.DeniedCategories.Contains(id);

        // DOCUMENT_CREATE depends on nothing else in the catalog, but filing into a folder the
        // caller cannot even open would be a trap, so the UI offers it only where both hold.
        bool CanCreate(Guid id) =>
            CanView(id) && create.AllowedCategories.Contains(id) && !create.DeniedCategories.Contains(id);

        // A folder name can itself be sensitive. Show a category when the caller can browse it,
        // or when it is on the path to one they can browse, so the tree stays navigable.
        var byId = all.ToDictionary(category => category.Id);
        var shown = new HashSet<Guid>();
        foreach (var category in all.Where(category => isCategoryAdmin || CanView(category.Id)))
        {
            for (var current = category; current is not null && shown.Add(current.Id);)
            {
                current = current.ParentId is { } parent ? byId.GetValueOrDefault(parent) : null;
            }
        }

        IReadOnlyList<CategoryNodeDto> result = all
            .Where(category => shown.Contains(category.Id))
            .Select(category => new CategoryNodeDto(
                category.Id,
                category.ParentId,
                category.Name,
                category.Code,
                category.Description,
                category.Depth,
                category.IsActive,
                category.SortOrder,
                CanView(category.Id),
                CanCreate(category.Id) && category.IsActive))
            .ToList();

        return Result.Success(result);
    }
}
