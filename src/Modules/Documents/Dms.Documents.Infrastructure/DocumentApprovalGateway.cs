using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure;

/// <summary>
/// Workflow's window onto versions. Looks at entities already tracked in this transaction first,
/// because AUTO_ON_VERSION starts a workflow on a version that was created a moment ago and is not
/// in the database yet.
/// </summary>
public sealed class DocumentApprovalGateway(
    DocumentsDbContext context,
    IDocumentRepository documents,
    TimeProvider timeProvider) : IDocumentApprovalGateway
{
    public async Task<VersionForWorkflow?> FindVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var id = new DocumentVersionId(versionId);
        var version = context.DocumentVersions.Local.FirstOrDefault(candidate => candidate.Id == id)
            ?? await context.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (version is null)
        {
            return null;
        }

        var document = context.Documents.Local.FirstOrDefault(candidate => candidate.Id == version.DocumentId)
            ?? await context.Documents.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == version.DocumentId, cancellationToken);

        return document is null
            ? null
            : new VersionForWorkflow(
                document.Id.Value,
                document.Title,
                version.Id.Value,
                version.VersionNumber,
                version.RevisionNumber,
                document.DocumentTypeId.Value,
                version.DocumentTypeVersionId.Value,
                version.DynamicData,
                version.CreatedBy,
                document.OwnerId,
                version.ApprovalStatus,
                document.IsDeleted);
    }

    public async Task RecordAsync(Guid documentId, Guid versionId, ApprovalStatus status, CancellationToken cancellationToken)
    {
        var id = new SharedKernel.DocumentId(documentId);
        var document = context.Documents.Local.FirstOrDefault(candidate => candidate.Id == id && candidate.Versions.Count > 0)
            ?? await documents.FindForUpdateAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Document {documentId} is not available for a workflow outcome.");

        var recorded = document.RecordApproval(new DocumentVersionId(versionId), status, timeProvider.GetUtcNow());
        if (recorded.IsFailure)
        {
            throw new InvalidOperationException(recorded.Error.Message);
        }
    }
}
