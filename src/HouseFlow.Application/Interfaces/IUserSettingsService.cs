using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

public interface IUserSettingsService
{
    Task<UserSettingsDto> GetSettingsAsync(Guid userId);
    Task<UserSettingsDto> UpdateSettingsAsync(Guid userId, UpdateUserSettingsDto settings);
}
