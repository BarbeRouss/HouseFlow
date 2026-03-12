using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

public interface IDeviceService
{
    Task<IEnumerable<DeviceSummaryDto>> GetHouseDevicesAsync(Guid houseId, Guid userId);
    Task<DeviceDetailDto?> GetDeviceDetailAsync(Guid deviceId, Guid userId);
    Task<DeviceDto> CreateDeviceAsync(Guid houseId, CreateDeviceRequestDto request, Guid userId);
    Task<DeviceDto?> UpdateDeviceAsync(Guid deviceId, UpdateDeviceRequestDto request, Guid userId);
    Task<bool> DeleteDeviceAsync(Guid deviceId, Guid userId);
}
