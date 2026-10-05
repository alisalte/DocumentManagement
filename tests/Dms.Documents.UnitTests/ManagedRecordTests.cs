using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Shouldly;

namespace Dms.Documents.UnitTests;

public sealed class ManagedRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserId Actor = UserId.New();

    [Fact]
    public void Declaration_pins_the_final_version_and_freezes_metadata()
    {
        var (document, version) = CreateDocumentWithVersion();

        var record = ManagedRecord.Declare(
            document,
            version.Id,
            RecordClassId.New(),
            null,
            Actor,
            Now);

        record.IsSuccess.ShouldBeTrue();
        record.Value.FinalVersionId.ShouldBe(version.Id);
        record.Value.Status.ShouldBe(RecordStatus.Active);
        record.Value.IsImmutable.ShouldBeTrue();
        record.Value.MetadataFrozenAt.ShouldBe(Now);
    }

    [Fact]
    public void Lifecycle_transitions_follow_the_allowed_graph()
    {
        var (document, version) = CreateDocumentWithVersion();
        var record = ManagedRecord.Declare(document, version.Id, RecordClassId.New(), null, Actor, Now).Value;

        record.TransitionTo(RecordStatus.UnderRetention, "start", Actor, Now).IsSuccess.ShouldBeTrue();
        record.TransitionTo(RecordStatus.Expired, "elapsed", Actor, Now).IsSuccess.ShouldBeTrue();
        record.TransitionTo(RecordStatus.PendingDisposal, "review", Actor, Now).IsSuccess.ShouldBeTrue();
        // Destroyed is only set via MarkDestroyed after approved disposition (phase 10.4).
        record.TransitionTo(RecordStatus.Destroyed, "approved", Actor, Now).IsFailure.ShouldBeTrue();
        record.MarkDestroyed("disposition", Actor, Now).IsSuccess.ShouldBeTrue();
        record.Status.ShouldBe(RecordStatus.Destroyed);
        record.IsImmutable.ShouldBeTrue();

        var again = ManagedRecord.Declare(document, version.Id, RecordClassId.New(), null, Actor, Now).Value;
        var skipped = again.TransitionTo(RecordStatus.Destroyed, "skip", Actor, Now);
        skipped.IsFailure.ShouldBeTrue();
        skipped.Error.Code.ShouldBe("record.invalid_transition");
    }

    private static (Document Document, DocumentVersion Version) CreateDocumentWithVersion()
    {
        var document = Document.Create(
            "عنوان",
            null,
            DocumentTypeId.New(),
            CategoryId.New(),
            Actor,
            Actor,
            Now,
            1405);
        var version = document.AddContentVersion(
            StorageObjectId.New(),
            "a.pdf",
            "application/pdf",
            10,
            new byte[32],
            DocumentTypeVersionId.New(),
            "{}",
            "v1",
            ApprovalStatus.NotRequired,
            Actor,
            Now);
        return (document, version);
    }
}
