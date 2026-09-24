using Dms.Infrastructure.Persistence;
using Dms.Notifications.Application;
using Dms.Notifications.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Notifications.Infrastructure.Persistence;

public sealed class NotificationsDbContext : DbContext
{
    public const string Schema = "notify";

    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new NotificationId(value));
            entity.Property(notification => notification.UserId).HasColumnName("user_id")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(notification => notification.Type).HasColumnName("type")
                .HasMaxLength(Notification.MaxTypeLength).IsRequired();
            entity.Property(notification => notification.ActorId).HasColumnName("actor_id")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(notification => notification.DocumentId).HasColumnName("document_id");
            entity.Property(notification => notification.VersionId).HasColumnName("version_id");
            entity.Property(notification => notification.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(notification => notification.CreatedAt).HasColumnName("created_at");
            entity.Property(notification => notification.ReadAt).HasColumnName("read_at");

            // Section 4.9: the bell asks "how many unread", the list pages by time.
            entity.HasIndex(notification => new { notification.UserId, notification.CreatedAt }, "ix_notifications_user_created")
                .IsDescending(false, true);
            entity.HasIndex(notification => new { notification.UserId, notification.CreatedAt }, "ix_notifications_user_unread")
                .IsDescending(false, true)
                .HasFilter("read_at IS NULL");
        });
    }
}

public sealed class NotificationRepository(NotificationsDbContext context) : INotificationRepository
{
    public void Add(Notification notification) => context.Notifications.Add(notification);

    public async Task<IReadOnlyList<Notification>> ListAsync(
        UserId userId,
        bool unreadOnly,
        NotificationCursor? after,
        int take,
        CancellationToken cancellationToken)
    {
        // A row comparison, which LINQ cannot express over the typed id.
        var query = after is { } cursor
            ? context.Notifications.FromSqlInterpolated(
                $"SELECT * FROM notify.notifications WHERE (created_at, id) < ({cursor.CreatedAt}, {cursor.Id})")
            : context.Notifications;

        query = query.AsNoTracking().Where(notification => notification.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(notification => notification.ReadAt == null);
        }

        return await query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountUnreadAsync(UserId userId, CancellationToken cancellationToken) =>
        context.Notifications.CountAsync(notification => notification.UserId == userId && notification.ReadAt == null, cancellationToken);

    public Task<int> MarkReadAsync(
        UserId userId,
        IReadOnlyCollection<NotificationId>? ids,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query = context.Notifications.Where(notification => notification.UserId == userId && notification.ReadAt == null);
        if (ids is not null)
        {
            query = query.Where(notification => ids.Contains(notification.Id));
        }

        return query.ExecuteUpdateAsync(set => set.SetProperty(notification => notification.ReadAt, now), cancellationToken);
    }

    public Task<int> DeleteOldAsync(DateTimeOffset readBefore, DateTimeOffset unreadBefore, CancellationToken cancellationToken) =>
        context.Notifications
            .Where(notification => (notification.ReadAt != null && notification.ReadAt < readBefore) || notification.CreatedAt < unreadBefore)
            .ExecuteDeleteAsync(cancellationToken);
}
