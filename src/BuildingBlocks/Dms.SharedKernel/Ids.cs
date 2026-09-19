namespace Dms.SharedKernel;

/// <summary>
/// Strongly typed identifiers. All identifiers are UUID v7 so that they are time ordered,
/// which keeps B-tree index inserts local and avoids page splits on high-volume tables.
/// </summary>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct GroupId(Guid Value)
{
    public static GroupId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct AclEntryId(Guid Value)
{
    public static AclEntryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct AuditEntryId(Guid Value)
{
    public static AuditEntryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Reserved for the Documents module (phase 2); declared here because
/// Authorization and Audit contracts already refer to documents by identity.</summary>
public readonly record struct CategoryId(Guid Value)
{
    public static CategoryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct DocumentId(Guid Value)
{
    public static DocumentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
