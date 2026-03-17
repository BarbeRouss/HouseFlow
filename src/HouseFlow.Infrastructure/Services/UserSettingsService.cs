using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Infrastructure.Services;

public class UserSettingsService : IUserSettingsService
{
    private readonly HouseFlowDbContext _context;

    public UserSettingsService(HouseFlowDbContext context)
    {
        _context = context;
    }

    public async Task<UserSettingsDto> GetSettingsAsync(Guid userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found");

        return new UserSettingsDto(user.Theme, user.Language);
    }

    public async Task<UserSettingsDto> UpdateSettingsAsync(Guid userId, UpdateUserSettingsDto settings)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found");

        user.Theme = settings.Theme;
        user.Language = settings.Language;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new UserSettingsDto(user.Theme, user.Language);
    }
}
