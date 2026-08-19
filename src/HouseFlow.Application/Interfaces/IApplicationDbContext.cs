using HouseFlow.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HouseFlow.Application.Interfaces;

/// <summary>
/// Persistence port for the Application layer. Implemented by the concrete EF Core
/// DbContext in HouseFlow.Infrastructure, keeping application services free of any
/// dependency on the database provider.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<House> Houses { get; }
    DbSet<Device> Devices { get; }
    DbSet<MaintenanceType> MaintenanceTypes { get; }
    DbSet<MaintenanceInstance> MaintenanceInstances { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<HouseMember> HouseMembers { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<ApiKey> ApiKeys { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    void SetAuditContext(Guid? userId, string? username, string? ipAddress = null, string? userAgent = null);
}
