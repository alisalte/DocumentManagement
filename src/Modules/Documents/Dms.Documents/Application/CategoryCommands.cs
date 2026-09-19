using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

public sealed record CreateCategoryCommand(Guid? ParentId, string Name, string Code, string? Description)
    : ICommand<Result<Guid>>;

public sealed record UpdateCategoryCommand(Guid Id, string Name, string? Description, bool IsActive, int SortOrder)
    : ICommand<Result>;

public sealed record MoveCategoryCommand(Guid Id, Guid? NewParentId) : ICommand<Result>;

internal static class DocumentErrors
{
    public static readonly Error Unauthenticated =
        Error.Unauthorized("auth.unauthenticated", "The request is not authenticated.");

    public static Error Forbidden(string explanation) => Error.Forbidden("auth.forbidden", explanation);

    public static readonly Error CategoryNotFound =
        Error.NotFound("category.not_found", "The category does not exist.");

    /// <summary>
    /// Returned when the caller may not even know the document exists. The endpoint turns this
    /// into a 404 so that probing ids reveals nothing.
    /// </summary>
    public static readonly Error DocumentNotFound =
        Error.NotFound("document.not_found", "The document does not exist.");

    public static readonly Error VersionNotFound =
        Error.NotFound("version.not_found", "The version does not exist.");
}

public sealed class CreateCategoryHandler(
    IDmsAuthorizer authorizer,
    ICategoryRepository categories,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreateCategoryCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        var decision = await authorizer.AuthorizeSystemAsync(
            PermissionCodes.AdminManageCategories,
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(DocumentErrors.Forbidden(decision.Explanation));
        }

        Category? parent = null;
        if (command.ParentId is { } parentId)
        {
            parent = await categories.FindAsync(new CategoryId(parentId), cancellationToken);
            if (parent is null)
            {
                return Result.Failure<Guid>(DocumentErrors.CategoryNotFound);
            }
        }

        var code = command.Code.Trim().ToUpperInvariant();
        if (await categories.FindSiblingByCodeAsync(parent?.Id, code, cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "category.duplicate_code",
                "A sibling category already uses this code."));
        }

        var created = Category.Create(
            parent,
            command.Name,
            command.Code,
            command.Description,
            actor,
            timeProvider.GetUtcNow());

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        categories.Add(created.Value);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.CategoryCreated,
                EntityType = "Category",
                EntityId = created.Value.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["code"] = created.Value.Code,
                    ["parentId"] = command.ParentId,
                },
            },
            cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }
}

public sealed class UpdateCategoryHandler(
    IDmsAuthorizer authorizer,
    ICategoryRepository categories,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<UpdateCategoryCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(
            PermissionCodes.AdminManageCategories,
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden(decision.Explanation));
        }

        var category = await categories.FindAsync(new CategoryId(command.Id), cancellationToken);
        if (category is null)
        {
            return Result.Failure(DocumentErrors.CategoryNotFound);
        }

        var now = timeProvider.GetUtcNow();
        category.Rename(command.Name, command.Description, now);
        category.SetActive(command.IsActive, now);
        category.SetSortOrder(command.SortOrder, now);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.CategoryUpdated,
                EntityType = "Category",
                EntityId = command.Id,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class MoveCategoryHandler(
    IDmsAuthorizer authorizer,
    ICategoryRepository categories,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<MoveCategoryCommand, Result>
{
    public async Task<Result> HandleAsync(MoveCategoryCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(
            PermissionCodes.AdminManageCategories,
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden(decision.Explanation));
        }

        var category = await categories.FindAsync(new CategoryId(command.Id), cancellationToken);
        if (category is null)
        {
            return Result.Failure(DocumentErrors.CategoryNotFound);
        }

        Category? newParent = null;
        if (command.NewParentId is { } parentId)
        {
            newParent = await categories.FindAsync(new CategoryId(parentId), cancellationToken);
            if (newParent is null)
            {
                return Result.Failure(DocumentErrors.CategoryNotFound);
            }
        }

        var oldPath = category.Path;
        var moved = category.MoveTo(newParent, timeProvider.GetUtcNow());
        if (moved.IsFailure)
        {
            return moved;
        }

        await categories.RepathDescendantsAsync(oldPath, category.Path, cancellationToken);

        // Moving a folder changes inherited permissions for everything below it, so it is audited
        // with both paths.
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.CategoryMoved,
                EntityType = "Category",
                EntityId = command.Id,
                Metadata = new Dictionary<string, object?>
                {
                    ["fromPath"] = oldPath,
                    ["toPath"] = category.Path,
                },
            },
            cancellationToken);

        return Result.Success();
    }
}
