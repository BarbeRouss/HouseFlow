using System.Text.Json;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Entities.Common;
using HouseFlow.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HouseFlow.Infrastructure.Data;

public class HouseFlowDbContext : DbContext
{
    private Guid? _currentUserId;
    private string? _currentUsername;
    private string? _currentIpAddress;
    private string? _currentUserAgent;

    public HouseFlowDbContext(DbContextOptions<HouseFlowDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<House> Houses => Set<House>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<MaintenanceType> MaintenanceTypes => Set<MaintenanceType>();
    public DbSet<MaintenanceInstance> MaintenanceInstances => Set<MaintenanceInstance>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<HouseMember> HouseMembers => Set<HouseMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Set the current user context for audit trail
    /// </summary>
    public void SetAuditContext(Guid? userId, string? username, string? ipAddress = null, string? userAgent = null)
    {
        _currentUserId = userId;
        _currentUsername = username;
        _currentIpAddress = ipAddress;
        _currentUserAgent = userAgent;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Configure all DateTime properties to be stored as UTC in PostgreSQL
        // This prevents "Cannot write DateTime with Kind=Unspecified" errors
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();

        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<UtcNullableDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Theme).IsRequired().HasMaxLength(20).HasDefaultValue("system");
            entity.Property(e => e.Language).IsRequired().HasMaxLength(10).HasDefaultValue("fr");
        });

        // House configuration
        modelBuilder.Entity<House>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.ZipCode).HasMaxLength(20);
            entity.Property(e => e.City).HasMaxLength(200);
            entity.Property(e => e.Country).HasMaxLength(100);
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Houses)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Device configuration
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Brand).HasMaxLength(200);
            entity.Property(e => e.Model).HasMaxLength(200);
            entity.HasIndex(e => e.HouseId);
            entity.HasOne(e => e.House)
                .WithMany(h => h.Devices)
                .HasForeignKey(e => e.HouseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MaintenanceType configuration
        modelBuilder.Entity<MaintenanceType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.DeviceId);
            entity.HasOne(e => e.Device)
                .WithMany(d => d.MaintenanceTypes)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MaintenanceInstance configuration
        modelBuilder.Entity<MaintenanceInstance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Cost).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Provider).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.HasIndex(e => e.MaintenanceTypeId);
            entity.HasIndex(e => e.Date);
            entity.HasOne(e => e.MaintenanceType)
                .WithMany(mt => mt.MaintenanceInstances)
                .HasForeignKey(e => e.MaintenanceTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // HouseMember configuration
        modelBuilder.Entity<HouseMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.HasIndex(e => new { e.UserId, e.HouseId }).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany(u => u.HouseMemberships)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.House)
                .WithMany(h => h.Members)
                .HasForeignKey(e => e.HouseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Invitation configuration
        modelBuilder.Entity<Invitation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.Property(e => e.Role).IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.Status).IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.HasIndex(e => e.HouseId);
            entity.HasOne(e => e.House)
                .WithMany(h => h.Invitations)
                .HasForeignKey(e => e.HouseId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.AcceptedByUser)
                .WithMany()
                .HasForeignKey(e => e.AcceptedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ApiKey configuration
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Prefix).IsRequired().HasMaxLength(15);
            entity.HasIndex(e => e.Prefix);
            entity.Property(e => e.KeyHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Scope).IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.ApiKeys)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuditLog configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(255);
            entity.Property(e => e.EntityId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.UserId);
        });

        // Configure global query filter for soft delete
        // Apply to all entities that implement ISoftDeletable
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                // Create the filter expression: entity => !entity.IsDeleted
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                var filterExpression = System.Linq.Expressions.Expression.Lambda(
                    System.Linq.Expressions.Expression.Not(property),
                    parameter
                );
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filterExpression);
            }
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditEntries = OnBeforeSaveChanges();
        var result = await base.SaveChangesAsync(cancellationToken);
        await OnAfterSaveChanges(auditEntries);
        return result;
    }

    private List<AuditEntry> OnBeforeSaveChanges()
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            // Skip audit logs themselves and certain entity types
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            // Handle audit trail properties
            if (entry.Entity is IAuditable auditableEntity)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditableEntity.CreatedAt = now;
                        auditableEntity.CreatedBy = _currentUserId;
                        auditableEntity.ModifiedAt = null;
                        auditableEntity.ModifiedBy = null;
                        break;

                    case EntityState.Modified:
                        auditableEntity.ModifiedAt = now;
                        auditableEntity.ModifiedBy = _currentUserId;
                        // Prevent modification of CreatedAt and CreatedBy
                        entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                        entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                        break;
                }
            }

            // Handle soft delete
            if (entry.Entity is ISoftDeletable softDeletableEntity && entry.State == EntityState.Deleted)
            {
                // Convert hard delete to soft delete
                entry.State = EntityState.Modified;
                softDeletableEntity.IsDeleted = true;
                softDeletableEntity.DeletedAt = now;
                softDeletableEntity.DeletedBy = _currentUserId;
            }

            // Create audit log entry
            var auditEntry = new AuditEntry(entry)
            {
                EntityType = entry.Entity.GetType().Name,
                Action = entry.State.ToString(),
                UserId = _currentUserId,
                Username = _currentUsername,
                Timestamp = now,
                IpAddress = _currentIpAddress,
                UserAgent = _currentUserAgent
            };

            auditEntries.Add(auditEntry);

            // For Added entities, we need to wait until after SaveChanges to get the ID
            foreach (var property in entry.Properties)
            {
                string propertyName = property.Metadata.Name;

                // Skip audit trail properties from being logged
                if (propertyName == nameof(IAuditable.CreatedAt) ||
                    propertyName == nameof(IAuditable.CreatedBy) ||
                    propertyName == nameof(IAuditable.ModifiedAt) ||
                    propertyName == nameof(IAuditable.ModifiedBy) ||
                    propertyName == nameof(ISoftDeletable.IsDeleted) ||
                    propertyName == nameof(ISoftDeletable.DeletedAt) ||
                    propertyName == nameof(ISoftDeletable.DeletedBy))
                    continue;

                if (entry.State == EntityState.Added)
                {
                    auditEntry.NewValues[propertyName] = property.CurrentValue;
                }
                else if (entry.State == EntityState.Deleted)
                {
                    auditEntry.OldValues[propertyName] = property.OriginalValue;
                }
                else if (entry.State == EntityState.Modified && property.IsModified)
                {
                    auditEntry.ChangedProperties.Add(propertyName);
                    auditEntry.OldValues[propertyName] = property.OriginalValue;
                    auditEntry.NewValues[propertyName] = property.CurrentValue;
                }
            }
        }

        return auditEntries;
    }

    private async Task OnAfterSaveChanges(List<AuditEntry> auditEntries)
    {
        if (auditEntries == null || auditEntries.Count == 0)
            return;

        foreach (var auditEntry in auditEntries)
        {
            // Get the entity ID after save (for Added entities)
            var idProperty = auditEntry.Entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
            if (idProperty != null)
            {
                auditEntry.EntityId = idProperty.CurrentValue?.ToString() ?? "";
            }

            AuditLogs.Add(auditEntry.ToAuditLog());
        }

        await base.SaveChangesAsync();
    }
}

/// <summary>
/// Temporary class for building audit log entries
/// </summary>
internal class AuditEntry
{
    public AuditEntry(EntityEntry entry)
    {
        Entry = entry;
    }

    public EntityEntry Entry { get; }
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Dictionary<string, object?> OldValues { get; } = new();
    public Dictionary<string, object?> NewValues { get; } = new();
    public List<string> ChangedProperties { get; } = new();

    public AuditLog ToAuditLog()
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = EntityType,
            EntityId = EntityId,
            Action = Action,
            UserId = UserId,
            Username = Username,
            Timestamp = Timestamp,
            IpAddress = IpAddress,
            UserAgent = UserAgent,
            OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues),
            NewValues = NewValues.Count == 0 ? null : JsonSerializer.Serialize(NewValues),
            ChangedProperties = ChangedProperties.Count == 0 ? null : JsonSerializer.Serialize(ChangedProperties)
        };
    }
}

/// <summary>
/// Converts DateTime values to UTC for PostgreSQL compatibility
/// Handles DateTimeKind.Unspecified by treating it as UTC
/// </summary>
internal class UtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

/// <summary>
/// Converts nullable DateTime values to UTC for PostgreSQL compatibility
/// </summary>
internal class UtcNullableDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>
{
    public UtcNullableDateTimeConverter()
        : base(
            v => v.HasValue
                ? (v.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v.Value.ToUniversalTime())
                : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}
