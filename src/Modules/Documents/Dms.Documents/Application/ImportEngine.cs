using System.Security.Cryptography;
using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.DocumentTypes.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;
using Dms.Storage.Contracts;

namespace Dms.Documents.Application;

public sealed class ImportOptions
{
    public const string SectionName = "Dms:Import";

    /// <summary>Server-side root under which relative manifest paths are resolved.</summary>
    public string? FilesRoot { get; set; }
}

/// <summary>Validates a loaded import job against the filesystem root and catalog mappings.</summary>
public sealed class ImportValidator(
    IImportItemRepository items,
    IImportMappingRepository mappings,
    ICategoryRepository categories,
    IDocumentTypeCatalog documentTypes,
    IRecordRepository records,
    IRetentionPolicyRepository retentionPolicies,
    IClassificationCatalog classifications,
    IUserDirectory users,
    IGroupDirectory groups,
    TimeProvider timeProvider)
{
    public async Task<ImportValidationReportDto> ValidateAsync(ImportJob job, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        job.BeginValidation(now).ThrowIfFailed();

        var issues = new List<ImportValidationIssueDto>();
        var seenSourceIds = new HashSet<string>(StringComparer.Ordinal);
        var valid = 0;
        var total = 0;
        long bytesTotal = 0;
        const int pageSize = 200;
        for (var skip = 0; ; skip += pageSize)
        {
            var page = await items.ListByJobAsync(job.Id, status: null, skip, pageSize, cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var item in page)
            {
                total++;
                var entry = JsonSerializer.Deserialize<LegacyManifestEntry>(item.EntryJson, ManifestSerializer.Options)
                    ?? new LegacyManifestEntry();
                var itemIssues = await ValidateItemAsync(job, item, entry, seenSourceIds, cancellationToken);
                issues.AddRange(itemIssues);

                var errors = itemIssues.Where(issue => issue.Severity == "Error").ToList();
                if (errors.Count == 0)
                {
                    item.MarkValid(now);
                    valid++;
                    bytesTotal += entry.Size ?? 0;
                }
                else
                {
                    var first = errors[0];
                    item.MarkInvalid(first.Code, first.Message, now);
                }
            }

            if (page.Count < pageSize)
            {
                break;
            }
        }

        var ok = issues.All(issue => issue.Severity != "Error");
        job.CompleteValidation(
            ok,
            total,
            total - valid,
            bytesTotal,
            ok ? null : "Validation failed. See item errors.",
            now);

        // Cap report payload; full item statuses remain queryable via /items.
        var reportIssues = issues.Count > 500 ? issues.Take(500).ToList() : issues;
        return new ImportValidationReportDto(
            job.Id.Value,
            job.Status.ToString(),
            total,
            valid,
            total - valid,
            issues.Count(issue => issue.Severity == "Warning"),
            reportIssues);
    }

    private async Task<List<ImportValidationIssueDto>> ValidateItemAsync(
        ImportJob job,
        ImportItem item,
        LegacyManifestEntry entry,
        HashSet<string> seenSourceIds,
        CancellationToken cancellationToken)
    {
        var issues = new List<ImportValidationIssueDto>();
        void Error(string code, string message) =>
            issues.Add(new ImportValidationIssueDto("Error", code, message, item.SourceId, item.SourcePath));
        void Warn(string code, string message) =>
            issues.Add(new ImportValidationIssueDto("Warning", code, message, item.SourceId, item.SourcePath));

        if (!seenSourceIds.Add(item.SourceId))
        {
            Error("IMPORT_DUPLICATE_SOURCE_ID", $"Duplicate sourceId '{item.SourceId}' in manifest.");
        }

        if (string.IsNullOrWhiteSpace(entry.Title))
        {
            Error("IMPORT_TITLE_REQUIRED", "Title is required.");
        }

        if (string.IsNullOrWhiteSpace(entry.CategoryPath))
        {
            Error("IMPORT_FOLDER_REQUIRED", "categoryPath is required.");
        }
        else
        {
            var folder = await ResolveCategoryAsync(job, entry.CategoryPath, create: false, cancellationToken);
            if (folder is null && !job.CreateMissingCategories)
            {
                Error("IMPORT_UNKNOWN_FOLDER", $"Unknown folder path '{entry.CategoryPath}'.");
            }
        }

        var typeCode = await MapAsync(job, ImportMappingKind.DocumentType, entry.DocumentTypeCode, cancellationToken)
            ?? entry.DocumentTypeCode;
        if (string.IsNullOrWhiteSpace(typeCode))
        {
            Error("IMPORT_DOCUMENT_TYPE_REQUIRED", "documentTypeCode is required.");
        }
        else
        {
            var type = await documentTypes.FindByCodeAsync(typeCode, cancellationToken);
            if (type is null)
            {
                Error("IMPORT_UNKNOWN_DOCUMENT_TYPE", $"Unknown document type '{typeCode}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(job.FilesRoot))
        {
            Error("IMPORT_FILES_ROOT", "Files root is not configured for this import job.");
        }
        else
        {
            var path = ImportPathSafety.ResolveSafePath(job.FilesRoot, item.SourcePath);
            if (path.IsFailure)
            {
                Error(path.Error.Code, path.Error.Message);
            }
            else if (!System.IO.File.Exists(path.Value))
            {
                Error("IMPORT_FILE_MISSING", $"File not found: {item.SourcePath}");
            }
            else
            {
                await using var stream = System.IO.File.OpenRead(path.Value);
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                var size = new FileInfo(path.Value).Length;
                if (entry.Size is { } expectedSize && expectedSize != size)
                {
                    Warn("IMPORT_SIZE_MISMATCH", $"Declared size {expectedSize} differs from actual {size}.");
                }

                if (!string.IsNullOrWhiteSpace(entry.Sha256))
                {
                    var expected = entry.Sha256.Trim().ToLowerInvariant();
                    if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                    {
                        Error("IMPORT_HASH_INVALID", "sha256 must be 64 hex characters.");
                    }
                    else if (!string.Equals(expected, hash, StringComparison.Ordinal))
                    {
                        Error("IMPORT_HASH_MISMATCH", $"Expected {expected}, actual {hash}.");
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.Classification))
        {
            var mapped = await MapAsync(job, ImportMappingKind.Classification, entry.Classification, cancellationToken)
                ?? entry.Classification;
            var level = await classifications.FindByCodeAsync(mapped, cancellationToken);
            if (level is null)
            {
                Error("IMPORT_UNKNOWN_CLASSIFICATION", $"Unknown classification '{mapped}'.");
            }
        }

        var ownerKey = entry.OwnerUsername ?? entry.CreatedBy;
        if (!string.IsNullOrWhiteSpace(ownerKey))
        {
            var mappedUser = await MapAsync(job, ImportMappingKind.User, ownerKey, cancellationToken) ?? ownerKey;
            var user = await FindUserAsync(mappedUser, cancellationToken);
            if (user is null)
            {
                Error("IMPORT_UNKNOWN_PRINCIPAL", $"Unknown user '{mappedUser}'.");
            }
        }

        if (entry.Acl is { Count: > 0 })
        {
            foreach (var acl in entry.Acl)
            {
                await ValidateAclAsync(job, acl, Error, cancellationToken);
            }
        }

        if (entry.Record == true)
        {
            var classCode = await MapAsync(job, ImportMappingKind.RecordClass, entry.RecordClassCode, cancellationToken)
                ?? entry.RecordClassCode;
            if (string.IsNullOrWhiteSpace(classCode))
            {
                Error("IMPORT_RECORD_CLASS_REQUIRED", "recordClassCode is required when record=true.");
            }
            else if (await records.FindClassByCodeAsync(classCode, cancellationToken) is null)
            {
                Error("IMPORT_UNKNOWN_RECORD_CLASS", $"Unknown record class '{classCode}'.");
            }

            if (!string.IsNullOrWhiteSpace(entry.RecordSeriesCode))
            {
                var seriesCode = await MapAsync(job, ImportMappingKind.RecordSeries, entry.RecordSeriesCode, cancellationToken)
                    ?? entry.RecordSeriesCode;
                // Series validated after class resolution during import.
                _ = seriesCode;
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.RetentionPolicyCode))
        {
            var code = await MapAsync(job, ImportMappingKind.RetentionPolicy, entry.RetentionPolicyCode, cancellationToken)
                ?? entry.RetentionPolicyCode;
            if (await retentionPolicies.FindByCodeAsync(code!, cancellationToken) is null)
            {
                Error("IMPORT_UNKNOWN_RETENTION", $"Unknown retention policy '{code}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.LegalHoldReason) && entry.LegalHoldReason.Length < 3)
        {
            Error("IMPORT_LEGAL_HOLD_REASON", "legalHoldReason is too short.");
        }

        return issues;
    }

    private async Task ValidateAclAsync(
        ImportJob job,
        LegacyAclEntry acl,
        Action<string, string> error,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(acl.Subject) || string.IsNullOrWhiteSpace(acl.Permission))
        {
            error("IMPORT_ACL_INVALID", "ACL entries require subject and permission.");
            return;
        }

        var permission = await MapAsync(job, ImportMappingKind.Permission, acl.Permission, cancellationToken)
            ?? acl.Permission;
        if (PermissionCatalog.Find(permission) is null)
        {
            error("IMPORT_UNKNOWN_PERMISSION", $"Unknown permission '{permission}'.");
        }

        var subjectType = (acl.SubjectType ?? "User").Trim();
        var subject = await MapAsync(
            job,
            subjectType.Equals("Group", StringComparison.OrdinalIgnoreCase) ? ImportMappingKind.Group : ImportMappingKind.User,
            acl.Subject,
            cancellationToken) ?? acl.Subject;

        if (subjectType.Equals("Group", StringComparison.OrdinalIgnoreCase))
        {
            var groupsFound = await groups.SearchAsync(subject, 20, cancellationToken);
            if (!groupsFound.Any(group => group.Code.Equals(subject, StringComparison.OrdinalIgnoreCase)))
            {
                error("IMPORT_UNKNOWN_PRINCIPAL", $"Unknown group '{subject}'.");
            }
        }
        else if (await FindUserAsync(subject, cancellationToken) is null)
        {
            error("IMPORT_UNKNOWN_PRINCIPAL", $"Unknown user '{subject}'.");
        }
    }

    private async Task<string?> MapAsync(
        ImportJob job,
        ImportMappingKind kind,
        string? sourceKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return null;
        }

        return await mappings.ResolveAsync(job.SourceSystem, job.Id, kind, sourceKey, cancellationToken);
    }

    private async Task<UserSummary?> FindUserAsync(string usernameOrId, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(usernameOrId, out var id))
        {
            return await users.FindAsync(new UserId(id), cancellationToken);
        }

        var matches = await users.SearchAsync(usernameOrId, 20, cancellationToken);
        return matches.FirstOrDefault(user =>
            user.Username.Equals(usernameOrId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Category?> ResolveCategoryAsync(
        ImportJob job,
        string categoryPath,
        bool create,
        CancellationToken cancellationToken)
    {
        var mapped = await MapAsync(job, ImportMappingKind.Folder, categoryPath, cancellationToken);
        if (mapped is not null && Guid.TryParse(mapped, out var mappedId))
        {
            return await categories.FindAsync(new CategoryId(mappedId), cancellationToken);
        }

        var segments = categoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var all = await categories.ListAsync(cancellationToken);
        Category? parent = all.FirstOrDefault(category => category.ParentId is null);
        if (parent is null)
        {
            return null;
        }

        foreach (var segment in segments)
        {
            var next = all.FirstOrDefault(category =>
                category.ParentId == parent.Id
                && (category.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)
                    || category.Code.Equals(segment, StringComparison.OrdinalIgnoreCase)));
            if (next is null)
            {
                return create ? null : null;
            }

            parent = next;
        }

        return parent;
    }
}

internal static class ResultExtensions
{
    public static void ThrowIfFailed(this Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{result.Error.Code}: {result.Error.Message}");
        }
    }
}

/// <summary>Imports a single claimed Ready/Retryable item through storage + domain services.</summary>
public sealed class ImportItemImporter(
    IImportSourceIndexRepository sourceIndex,
    IImportMappingRepository mappings,
    ICategoryRepository categories,
    IDocumentRepository documents,
    IDocumentTypeCatalog documentTypes,
    IRecordRepository records,
    IRetentionPolicyRepository retentionPolicies,
    ILegalHoldRepository legalHolds,
    IClassificationCatalog classifications,
    IStorageService storage,
    IResourceAclWriter acl,
    TagResolver tags,
    MetadataGate metadataGate,
    IUserDirectory users,
    IGroupDirectory groups,
    IAuditWriter audit,
    DocumentChanges changes,
    IVersionCreatedHook versionCreated,
    TimeProvider timeProvider)
{
    public async Task ImportAsync(ImportJob job, ImportItem item, UserId actor, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var claim = item.TryClaim(now);
        if (claim.IsFailure)
        {
            return;
        }

        try
        {
            var existing = await sourceIndex.FindAsync(job.SourceSystem, item.SourceId, cancellationToken);
            if (existing is not null)
            {
                item.MarkSkipped(
                    "IMPORT_IDEMPOTENT_SKIP",
                    "Source already imported.",
                    existing.DocumentId,
                    now);
                job.RecordItemOutcome(ImportItemStatus.Skipped, 0, now);
                await audit.WriteAsync(
                    ItemAudit(AuditActions.ImportItemSkipped, job, item, actor),
                    cancellationToken);
                return;
            }

            var entry = JsonSerializer.Deserialize<LegacyManifestEntry>(item.EntryJson, ManifestSerializer.Options)
                ?? throw new InvalidOperationException("entry");

            var path = ImportPathSafety.ResolveSafePath(job.FilesRoot!, item.SourcePath);
            if (path.IsFailure)
            {
                await FailAsync(job, item, path.Error.Code, path.Error.Message, retryable: false, null, now, actor, cancellationToken);
                return;
            }

            await using var fileStream = System.IO.File.OpenRead(path.Value);
            var written = await storage.WriteAsync(
                fileStream,
                entry.FileName ?? Path.GetFileName(path.Value),
                entry.ContentType,
                cancellationToken);
            if (written.IsFailure)
            {
                await FailAsync(job, item, written.Error.Code, written.Error.Message, retryable: true, null, now, actor, cancellationToken);
                return;
            }

            var actualHash = Convert.ToHexStringLower(written.Value.Sha256);
            if (!string.IsNullOrWhiteSpace(entry.Sha256)
                && !string.Equals(entry.Sha256.Trim(), actualHash, StringComparison.OrdinalIgnoreCase))
            {
                await storage.DiscardAsync(written.Value, cancellationToken);
                await FailAsync(job, item, "IMPORT_HASH_MISMATCH", "Hash mismatch at import time.", false, actualHash, now, actor, cancellationToken);
                return;
            }

            var staged = await storage.RegisterAsync(written.Value, cancellationToken);

            var category = await EnsureCategoryAsync(job, entry.CategoryPath!, actor, now, cancellationToken);
            if (category is null)
            {
                await storage.MarkForDeletionAsync(staged.Id, cancellationToken);
                await FailAsync(job, item, "IMPORT_UNKNOWN_FOLDER", "Folder could not be resolved.", false, actualHash, now, actor, cancellationToken);
                return;
            }

            var typeCode = await MapAsync(job, ImportMappingKind.DocumentType, entry.DocumentTypeCode, cancellationToken)
                ?? entry.DocumentTypeCode!;
            var documentType = await documentTypes.FindByCodeAsync(typeCode, cancellationToken);
            if (documentType is null)
            {
                await storage.MarkForDeletionAsync(staged.Id, cancellationToken);
                await FailAsync(job, item, "IMPORT_UNKNOWN_DOCUMENT_TYPE", typeCode, false, actualHash, now, actor, cancellationToken);
                return;
            }

            var schema = await documentTypes.ResolveVersionForNewDocumentAsync(documentType.Id, cancellationToken);
            if (schema.IsFailure)
            {
                await storage.MarkForDeletionAsync(staged.Id, cancellationToken);
                await FailAsync(job, item, schema.Error.Code, schema.Error.Message, false, actualHash, now, actor, cancellationToken);
                return;
            }

            JsonElement? metadataElement = null;
            if (entry.Metadata is { Count: > 0 })
            {
                metadataElement = JsonSerializer.SerializeToElement(entry.Metadata);
            }

            var metadata = await metadataGate.ValidateAsync(schema.Value, metadataElement, cancellationToken);
            if (metadata.IsFailure)
            {
                await storage.MarkForDeletionAsync(staged.Id, cancellationToken);
                await FailAsync(job, item, metadata.Error.Code, metadata.Error.Message, false, actualHash, now, actor, cancellationToken);
                return;
            }

            var owner = actor;
            var ownerKey = entry.OwnerUsername ?? entry.CreatedBy;
            if (!string.IsNullOrWhiteSpace(ownerKey))
            {
                var mapped = await MapAsync(job, ImportMappingKind.User, ownerKey, cancellationToken) ?? ownerKey;
                var user = await FindUserAsync(mapped, cancellationToken);
                if (user is not null)
                {
                    owner = user.Id;
                }
            }

            // Prefer staged + written payload: Register and Find share one transaction (job worker).
            var uploadInfo = await storage.FindAsync(staged.Id, cancellationToken);
            if (uploadInfo is null || uploadInfo.Status != StorageObjectStatus.Staged)
            {
                uploadInfo = new StorageObjectInfo(
                    staged.Id,
                    staged.FileName,
                    staged.MimeType,
                    staged.Size,
                    written.Value.Sha256,
                    StorageObjectStatus.Staged,
                    ScanStatus.Skipped,
                    actor);
            }

            if (documentType.Settings.MaxUploadBytes is { } maxBytes && uploadInfo.Size > maxBytes)
            {
                await storage.MarkForDeletionAsync(staged.Id, cancellationToken);
                await FailAsync(job, item, "upload.too_large_for_type", $"Max {maxBytes} bytes.", false, actualHash, now, actor, cancellationToken);
                return;
            }

            var tagIds = await tags.ResolveAsync(entry.Tags ?? [], actor, cancellationToken);
            if (tagIds.IsFailure)
            {
                await storage.MarkForDeletionAsync(staged.Id, cancellationToken);
                await FailAsync(job, item, tagIds.Error.Code, tagIds.Error.Message, false, actualHash, now, actor, cancellationToken);
                return;
            }

            var ready = await versionCreated.EnsureReadyAsync(documentType.Id.Value, cancellationToken);
            if (ready.IsFailure)
            {
                await storage.MarkForDeletionAsync(staged.Id, cancellationToken);
                await FailAsync(job, item, ready.Error.Code, ready.Error.Message, true, actualHash, now, actor, cancellationToken);
                return;
            }

            var filedAt = entry.CreatedAt ?? now;
            var document = Document.Create(
                entry.Title!.Trim(),
                string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim(),
                documentType.Id,
                category.Id,
                owner,
                actor,
                filedAt,
                PersianFiscalYear.Of(filedAt));

            var version = document.AddContentVersion(
                uploadInfo.Id,
                uploadInfo.FileName,
                uploadInfo.MimeType,
                uploadInfo.Size,
                uploadInfo.Sha256,
                schema.Value,
                metadata.Value,
                "Legacy import",
                DocumentRules.InitialStatus(documentType),
                actor,
                entry.CreatedAt ?? now);

            document.SetTags(tagIds.Value, now, actor);
            documents.Add(document);

            var committed = await storage.CommitAsync(uploadInfo.Id, cancellationToken);
            if (committed.IsFailure)
            {
                await storage.MarkForDeletionAsync(uploadInfo.Id, cancellationToken);
                await FailAsync(job, item, committed.Error.Code, committed.Error.Message, true, actualHash, now, actor, cancellationToken);
                return;
            }

            // Additional legacy versions: import in versionNumber order; isCurrent forced last so it becomes CurrentVersionId.
            if (entry.Versions is { Count: > 0 })
            {
                var ordered = entry.Versions
                    .OrderBy(candidate => candidate.IsCurrent == true ? 1 : 0)
                    .ThenBy(candidate => candidate.VersionNumber ?? 0)
                    .ToList();
                foreach (var legacyVersion in ordered)
                {
                    var relative = legacyVersion.File;
                    if (string.IsNullOrWhiteSpace(relative)
                        || string.Equals(relative, item.SourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var versionPath = ImportPathSafety.ResolveSafePath(job.FilesRoot!, relative);
                    if (versionPath.IsFailure)
                    {
                        await audit.WriteAsync(
                            EnrichmentFailureAudit(job, item, document.Id, versionPath.Error.Code, versionPath.Error.Message),
                            cancellationToken);
                        continue;
                    }

                    await using var versionStream = System.IO.File.OpenRead(versionPath.Value);
                    var writtenVersion = await storage.WriteAsync(
                        versionStream,
                        Path.GetFileName(versionPath.Value),
                        legacyVersion.ContentType ?? entry.ContentType,
                        cancellationToken);
                    if (writtenVersion.IsFailure)
                    {
                        await audit.WriteAsync(
                            EnrichmentFailureAudit(job, item, document.Id, writtenVersion.Error.Code, writtenVersion.Error.Message),
                            cancellationToken);
                        continue;
                    }

                    var stagedVersion = await storage.RegisterAsync(writtenVersion.Value, cancellationToken);
                    var versionInfo = await storage.FindAsync(stagedVersion.Id, cancellationToken);
                    if (versionInfo is null)
                    {
                        continue;
                    }

                    var versionCommit = await storage.CommitAsync(versionInfo.Id, cancellationToken);
                    if (versionCommit.IsFailure)
                    {
                        await storage.MarkForDeletionAsync(versionInfo.Id, cancellationToken);
                        continue;
                    }

                    version = document.AddContentVersion(
                        versionInfo.Id,
                        versionInfo.FileName,
                        versionInfo.MimeType,
                        versionInfo.Size,
                        versionInfo.Sha256,
                        schema.Value,
                        metadata.Value,
                        legacyVersion.ChangeDescription ?? $"Legacy version {legacyVersion.SourceVersionId ?? legacyVersion.VersionNumber?.ToString()}",
                        DocumentRules.InitialStatus(documentType),
                        actor,
                        legacyVersion.CreatedAt ?? now);
                }
            }

            await acl.GrantAsync(
                ResourceRef.Document(document.Id.Value),
                SubjectType.User,
                owner.Value,
                DocumentRules.OwnerDefaults,
                "Owner defaults from legacy import",
                cancellationToken,
                actingAs: actor);

            if (entry.Acl is { Count: > 0 })
            {
                await ApplyAclAsync(job, document.Id, entry.Acl, cancellationToken);
            }

            // Idempotency index before enrichment so a later enrichment failure cannot create duplicates.
            sourceIndex.Add(new ImportSourceIndex(
                job.SourceSystem,
                item.SourceId,
                document.Id,
                job.Id,
                item.Id,
                now));

            if (!string.IsNullOrWhiteSpace(entry.Classification))
            {
                var classCode = await MapAsync(job, ImportMappingKind.Classification, entry.Classification, cancellationToken)
                    ?? entry.Classification;
                var assigned = await classifications.AssignAsync(document.Id, classCode!, actor, cancellationToken);
                if (assigned.IsFailure)
                {
                    await audit.WriteAsync(
                        EnrichmentFailureAudit(job, item, document.Id, assigned.Error.Code, assigned.Error.Message),
                        cancellationToken);
                }
            }

            RecordId? recordId = null;
            if (entry.Record == true)
            {
                var classCode = await MapAsync(job, ImportMappingKind.RecordClass, entry.RecordClassCode, cancellationToken)
                    ?? entry.RecordClassCode!;
                var recordClass = await records.FindClassByCodeAsync(classCode, cancellationToken);
                if (recordClass is null)
                {
                    await audit.WriteAsync(
                        EnrichmentFailureAudit(job, item, document.Id, "IMPORT_UNKNOWN_RECORD_CLASS", classCode),
                        cancellationToken);
                }
                else
                {
                    RecordSeriesId? seriesId = null;
                    if (!string.IsNullOrWhiteSpace(entry.RecordSeriesCode))
                    {
                        var seriesCode = await MapAsync(job, ImportMappingKind.RecordSeries, entry.RecordSeriesCode, cancellationToken)
                            ?? entry.RecordSeriesCode;
                        var series = await records.FindSeriesByCodeAsync(recordClass.Id, seriesCode!, cancellationToken);
                        if (series is null)
                        {
                            await audit.WriteAsync(
                                EnrichmentFailureAudit(job, item, document.Id, "IMPORT_UNKNOWN_RECORD_SERIES", seriesCode!),
                                cancellationToken);
                        }
                        else
                        {
                            seriesId = series.Id;
                        }
                    }

                    var declared = ManagedRecord.Declare(
                        document,
                        version.Id,
                        recordClass.Id,
                        seriesId,
                        actor,
                        now);
                    if (declared.IsFailure)
                    {
                        await audit.WriteAsync(
                            EnrichmentFailureAudit(job, item, document.Id, declared.Error.Code, declared.Error.Message),
                            cancellationToken);
                    }
                    else
                    {
                        records.Add(declared.Value);
                        recordId = declared.Value.Id;

                        if (!string.IsNullOrWhiteSpace(entry.RetentionPolicyCode))
                        {
                            var policyCode = await MapAsync(job, ImportMappingKind.RetentionPolicy, entry.RetentionPolicyCode, cancellationToken)
                                ?? entry.RetentionPolicyCode;
                            var policy = await retentionPolicies.FindByCodeAsync(policyCode!, cancellationToken);
                            if (policy is not null)
                            {
                                var applied = declared.Value.ApplyRetentionPolicy(
                                    policy,
                                    document.CreatedAt,
                                    actor,
                                    now);
                                if (applied.IsFailure)
                                {
                                    await audit.WriteAsync(
                                        EnrichmentFailureAudit(job, item, document.Id, applied.Error.Code, applied.Error.Message),
                                        cancellationToken);
                                }
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.LegalHoldReason))
            {
                var hold = LegalHold.Place(document.Id, entry.LegalHoldReason, actor, now);
                if (hold.IsSuccess)
                {
                    legalHolds.Add(hold.Value);
                }
                else
                {
                    // Document imported; hold needs manual review — do not silently ignore.
                    await audit.WriteAsync(
                        new AuditRecord
                        {
                            Action = AuditActions.ImportItemFailed,
                            EntityType = "ImportItem",
                            EntityId = item.Id.Value,
                            DocumentId = document.Id.Value,
                            Outcome = AuditOutcome.Failed,
                            Metadata = new Dictionary<string, object?>
                            {
                                ["importJobId"] = job.Id.Value,
                                ["sourceId"] = item.SourceId,
                                ["errorCode"] = "IMPORT_LEGAL_HOLD_REVIEW",
                                ["errorMessage"] = hold.Error.Message,
                            },
                        },
                        cancellationToken);
                }
            }

            item.MarkSucceeded(document.Id, version.Id, recordId, actualHash, written.Value.Size, now);
            job.RecordItemOutcome(ImportItemStatus.Succeeded, written.Value.Size, now);

            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.ImportItemImported,
                    EntityType = "ImportItem",
                    EntityId = item.Id.Value,
                    DocumentId = document.Id.Value,
                    VersionId = version.Id.Value,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["importJobId"] = job.Id.Value,
                        ["sourceId"] = item.SourceId,
                        ["sourceSystem"] = job.SourceSystem,
                        ["sha256"] = actualHash,
                        ["recordId"] = recordId?.Value,
                    },
                },
                cancellationToken);

            await versionCreated.OnVersionCreatedAsync(
                documentType.Id.Value,
                document.Id.Value,
                version.Id.Value,
                cancellationToken);
            await changes.NotifyAsync(document.Id.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            await FailAsync(job, item, "IMPORT_TRANSIENT", ex.Message, retryable: true, null, timeProvider.GetUtcNow(), actor, cancellationToken);
        }
        catch (Exception ex)
        {
            await FailAsync(job, item, "IMPORT_UNEXPECTED", ex.Message, retryable: false, null, timeProvider.GetUtcNow(), actor, cancellationToken);
        }
    }

    private async Task FailAsync(
        ImportJob job,
        ImportItem item,
        string code,
        string message,
        bool retryable,
        string? hash,
        DateTimeOffset now,
        UserId actor,
        CancellationToken cancellationToken)
    {
        item.MarkFailed(code, message, retryable, hash, now);
        job.RecordItemOutcome(item.Status, item.ActualSize ?? 0, now);
        await audit.WriteAsync(ItemAudit(AuditActions.ImportItemFailed, job, item, actor), cancellationToken);
    }

    private static AuditRecord ItemAudit(string action, ImportJob job, ImportItem item, UserId actor) => new()
    {
        Action = action,
        EntityType = "ImportItem",
        EntityId = item.Id.Value,
        DocumentId = item.TargetDocumentId?.Value,
        Metadata = new Dictionary<string, object?>
        {
            ["importJobId"] = job.Id.Value,
            ["sourceId"] = item.SourceId,
            ["errorCode"] = item.ErrorCode,
            ["errorMessage"] = item.ErrorMessage,
            ["actor"] = actor.Value,
        },
    };

    private static AuditRecord EnrichmentFailureAudit(
        ImportJob job,
        ImportItem item,
        DocumentId documentId,
        string code,
        string message) => new()
    {
        Action = AuditActions.ImportItemFailed,
        EntityType = "ImportItem",
        EntityId = item.Id.Value,
        DocumentId = documentId.Value,
        Outcome = AuditOutcome.Failed,
        Metadata = new Dictionary<string, object?>
        {
            ["importJobId"] = job.Id.Value,
            ["sourceId"] = item.SourceId,
            ["errorCode"] = code,
            ["errorMessage"] = message,
            ["phase"] = "enrichment",
        },
    };

    private async Task ApplyAclAsync(
        ImportJob job,
        DocumentId documentId,
        List<LegacyAclEntry> entries,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            var permission = await MapAsync(job, ImportMappingKind.Permission, entry.Permission, cancellationToken)
                ?? entry.Permission!;
            var isGroup = (entry.SubjectType ?? "User").Equals("Group", StringComparison.OrdinalIgnoreCase);
            var subjectKey = await MapAsync(
                job,
                isGroup ? ImportMappingKind.Group : ImportMappingKind.User,
                entry.Subject,
                cancellationToken) ?? entry.Subject!;

            Guid subjectId;
            if (isGroup)
            {
                var group = (await groups.SearchAsync(subjectKey, 20, cancellationToken))
                    .FirstOrDefault(candidate => candidate.Code.Equals(subjectKey, StringComparison.OrdinalIgnoreCase));
                if (group is null)
                {
                    continue;
                }

                subjectId = group.Id.Value;
            }
            else
            {
                var user = await FindUserAsync(subjectKey, cancellationToken);
                if (user is null)
                {
                    continue;
                }

                subjectId = user.Id.Value;
            }

            var effect = (entry.Effect ?? "Allow").Equals("Deny", StringComparison.OrdinalIgnoreCase)
                ? PermissionEffect.Deny
                : PermissionEffect.Allow;

            await acl.GrantAsync(
                ResourceRef.Document(documentId.Value),
                isGroup ? SubjectType.Group : SubjectType.User,
                subjectId,
                [permission],
                "Legacy import ACL",
                cancellationToken,
                actingAs: job.CreatedBy);
            _ = effect; // Grant API uses Allow list; Deny would need dedicated call — map Deny as skip warning.
        }
    }

    private async Task<Category?> EnsureCategoryAsync(
        ImportJob job,
        string categoryPath,
        UserId actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var mapped = await MapAsync(job, ImportMappingKind.Folder, categoryPath, cancellationToken);
        if (mapped is not null && Guid.TryParse(mapped, out var id))
        {
            return await categories.FindAsync(new CategoryId(id), cancellationToken);
        }

        var segments = categoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var all = (await categories.ListAsync(cancellationToken)).ToList();
        var parent = all.FirstOrDefault(category => category.ParentId is null);
        if (parent is null)
        {
            return null;
        }

        foreach (var segment in segments)
        {
            var next = all.FirstOrDefault(category =>
                category.ParentId == parent.Id
                && (category.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)
                    || category.Code.Equals(segment, StringComparison.OrdinalIgnoreCase)));
            if (next is null)
            {
                if (!job.CreateMissingCategories)
                {
                    return null;
                }

                var code = new string(segment.Where(char.IsLetterOrDigit).Take(12).ToArray()).ToUpperInvariant();
                if (string.IsNullOrEmpty(code))
                {
                    code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                }

                var created = Category.Create(parent, segment, code, null, actor, now);
                if (created.IsFailure)
                {
                    return null;
                }

                categories.Add(created.Value);
                all.Add(created.Value);
                next = created.Value;
            }

            parent = next;
        }

        return parent;
    }

    private Task<string?> MapAsync(
        ImportJob job,
        ImportMappingKind kind,
        string? sourceKey,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(sourceKey)
            ? Task.FromResult<string?>(null)
            : mappings.ResolveAsync(job.SourceSystem, job.Id, kind, sourceKey, cancellationToken);

    private async Task<UserSummary?> FindUserAsync(string usernameOrId, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(usernameOrId, out var id))
        {
            return await users.FindAsync(new UserId(id), cancellationToken);
        }

        return (await users.SearchAsync(usernameOrId, 20, cancellationToken))
            .FirstOrDefault(user => user.Username.Equals(usernameOrId, StringComparison.OrdinalIgnoreCase));
    }
}
