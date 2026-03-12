using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

public interface IHouseService
{
    Task<HousesListResponseDto> GetUserHousesAsync(Guid userId);
    Task<HouseDetailDto?> GetHouseDetailAsync(Guid houseId, Guid userId);
    Task<HouseDto> CreateHouseAsync(CreateHouseRequestDto request, Guid userId);
    Task<HouseDto?> UpdateHouseAsync(Guid houseId, UpdateHouseRequestDto request, Guid userId);
    Task<bool> DeleteHouseAsync(Guid houseId, Guid userId);
}
