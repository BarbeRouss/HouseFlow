using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HouseFlow.Application.Services;

public class AdminService : IAdminService
{
    public const int MaxPageSize = 100;

    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminService> _logger;

    public AdminService(IApplicationDbContext context, IConfiguration configuration, ILogger<AdminService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AdminStatsDto> GetStatsAsync()
    {
        return new AdminStatsDto(
            Users: await _context.Users.CountAsync(),
            Admins: await _context.Users.CountAsync(u => u.IsAdmin),
            Houses: await _context.Houses.CountAsync(),
            Devices: await _context.Devices.CountAsync(),
            MaintenanceInstances: await _context.MaintenanceInstances.CountAsync());
    }

    public async Task<AdminUsersPageDto> GetUsersAsync(string? search, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(term) ||
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term));
        }

        var total = await query.CountAsync();

        var users = await query
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto(
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.IsAdmin,
                u.CreatedAt,
                u.Houses.Count))
            .ToListAsync();

        return new AdminUsersPageDto(users, total, page, pageSize);
    }

    public async Task<AdminUserDto> SetAdminAsync(Guid actingUserId, Guid targetUserId, bool isAdmin)
    {
        if (!isAdmin && actingUserId == targetUserId)
        {
            throw new InvalidOperationException("You cannot remove your own administrator rights.");
        }

        var user = await _context.Users
            .Include(u => u.Houses)
            .FirstOrDefaultAsync(u => u.Id == targetUserId)
            ?? throw new KeyNotFoundException("User not found");

        if (user.IsAdmin != isAdmin)
        {
            user.IsAdmin = isAdmin;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Admin rights {Action} for user {UserId} by {ActingUserId}",
                isAdmin ? "granted" : "revoked", targetUserId, actingUserId);
        }

        return ToDto(user);
    }

    public async Task<int> PromoteBootstrapAdminsAsync()
    {
        var emails = AdminBootstrap.GetBootstrapEmails(_configuration)
            .Select(e => e.ToLower())
            .ToArray();
        if (emails.Length == 0) return 0;

        var users = await _context.Users
            .Where(u => !u.IsAdmin && emails.Contains(u.Email.ToLower()))
            .ToListAsync();

        foreach (var user in users)
        {
            user.IsAdmin = true;
            user.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("Bootstrap admin promoted: {UserId}", user.Id);
        }

        if (users.Count > 0) await _context.SaveChangesAsync();
        return users.Count;
    }

    private static AdminUserDto ToDto(User u) =>
        new(u.Id, u.Email, u.FirstName, u.LastName, u.IsAdmin, u.CreatedAt, u.Houses.Count);
}
