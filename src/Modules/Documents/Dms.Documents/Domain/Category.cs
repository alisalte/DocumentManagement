using Dms.SharedKernel;

namespace Dms.Documents.Domain;

/// <summary>
/// A folder in the archive tree. The materialised <see cref="Path"/> (an ltree of category ids)
/// makes "everything under Maintenance" a single indexed query, which is what permission
/// inheritance and category listings both need.
/// </summary>
public sealed class Category : AggregateRoot<CategoryId>
{
    public const int MaxDepth = 12;

    private Category()
    {
    }

    private Category(
        CategoryId id,
        CategoryId? parentId,
        string name,
        string code,
        string? description,
        string path,
        int depth,
        UserId? createdBy,
        DateTimeOffset now)
        : base(id)
    {
        ParentId = parentId;
        Name = name;
        Code = code;
        Description = description;
        Path = path;
        Depth = depth;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public CategoryId? ParentId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Dot separated category ids from the root down to and including this category.</summary>
    public string Path { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    public bool IsActive { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>Null for categories seeded by the system, such as the root.</summary>
    public UserId? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Category> Create(
        Category? parent,
        string name,
        string code,
        string? description,
        UserId? createdBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Category>(Error.Validation("category.name_required", "A name is required."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Category>(Error.Validation("category.code_required", "A code is required."));
        }

        var depth = parent is null ? 0 : parent.Depth + 1;
        if (depth > MaxDepth)
        {
            return Result.Failure<Category>(Error.Validation(
                "category.too_deep",
                $"The category tree is limited to {MaxDepth} levels."));
        }

        var id = CategoryId.New();
        var label = ToLabel(id);
        var path = parent is null ? label : $"{parent.Path}.{label}";

        return Result.Success(new Category(
            id,
            parent?.Id,
            name.Trim(),
            code.Trim().ToUpperInvariant(),
            description,
            path,
            depth,
            createdBy,
            now));
    }

    /// <summary>ltree labels cannot contain a hyphen before PostgreSQL 16, so ids are written plain.</summary>
    public static string ToLabel(CategoryId id) => id.Value.ToString("N");

    /// <summary>Ancestor ids, nearest first, excluding this category.</summary>
    public IReadOnlyList<Guid> AncestorIds() => ParseAncestors(Path, Id);

    public static IReadOnlyList<Guid> ParseAncestors(string path, CategoryId self)
    {
        var labels = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var ancestors = new List<Guid>(labels.Length);
        foreach (var label in labels)
        {
            if (Guid.TryParseExact(label, "N", out var value) && value != self.Value)
            {
                ancestors.Add(value);
            }
        }

        ancestors.Reverse();
        return ancestors;
    }

    public void Rename(string name, string? description, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description;
        UpdatedAt = now;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
    }

    public void SetSortOrder(int sortOrder, DateTimeOffset now)
    {
        SortOrder = sortOrder;
        UpdatedAt = now;
    }

    /// <summary>
    /// Re-parents this category. Descendant paths are rewritten in one set-based statement by the
    /// repository; doing it row by row would be both slow and easy to leave half done.
    /// </summary>
    public Result MoveTo(Category? newParent, DateTimeOffset now)
    {
        if (newParent is not null)
        {
            if (newParent.Id == Id)
            {
                return Result.Failure(Error.Validation("category.cycle", "A category cannot contain itself."));
            }

            if (newParent.Path.StartsWith(Path + ".", StringComparison.Ordinal) || newParent.Path == Path)
            {
                return Result.Failure(Error.Validation(
                    "category.cycle",
                    "A category cannot be moved inside one of its own descendants."));
            }

            if (newParent.Depth + 1 > MaxDepth)
            {
                return Result.Failure(Error.Validation(
                    "category.too_deep",
                    $"The category tree is limited to {MaxDepth} levels."));
            }
        }

        ParentId = newParent?.Id;
        Depth = newParent is null ? 0 : newParent.Depth + 1;
        Path = newParent is null ? ToLabel(Id) : $"{newParent.Path}.{ToLabel(Id)}";
        UpdatedAt = now;
        return Result.Success();
    }
}
