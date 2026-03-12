using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

public interface IMaintenanceService
{
    Task<IEnumerable<MaintenanceTypeWithStatusDto>> GetDeviceMaintenanceTypesAsync(Guid deviceId, Guid userId);
    Task<MaintenanceTypeDto> CreateMaintenanceTypeAsync(Guid deviceId, CreateMaintenanceTypeRequestDto request, Guid userId);
    Task<MaintenanceTypeDto?> UpdateMaintenanceTypeAsync(Guid typeId, UpdateMaintenanceTypeRequestDto request, Guid userId);
    Task<bool> DeleteMaintenanceTypeAsync(Guid typeId, Guid userId);
    Task<MaintenanceInstanceDto> LogMaintenanceAsync(Guid typeId, LogMaintenanceRequestDto request, Guid userId);
    Task<MaintenanceHistoryResponseDto> GetDeviceMaintenanceHistoryAsync(Guid deviceId, Guid userId);
    Task<MaintenanceInstanceDto?> UpdateMaintenanceInstanceAsync(Guid instanceId, UpdateMaintenanceInstanceRequestDto request, Guid userId);
    Task<bool> DeleteMaintenanceInstanceAsync(Guid instanceId, Guid userId);
}
