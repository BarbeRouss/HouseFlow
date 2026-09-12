using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

public interface IAdminService
{
    Task<AdminStatsDto> GetStatsAsync();
    Task<AdminUsersPageDto> GetUsersAsync(string? search, int page, int pageSize);
    Task<AdminUserDto> SetAdminAsync(Guid actingUserId, Guid targetUserId, bool isAdmin);
    /// <summary>Grants the admin flag to every existing account listed in the bootstrap configuration.</summary>
    Task<int> PromoteBootstrapAdminsAsync();
}
