import { useQuery, useMutation, useQueryClient, UseQueryOptions, UseMutationOptions } from '@tanstack/react-query';
import apiClient from '../client';

// DTOs matching backend MVP model
interface CreateDeviceRequestDto {
  name: string;
  type: string;
  brand?: string | null;
  model?: string | null;
  installDate?: string | null;
}

interface UpdateDeviceRequestDto {
  name?: string | null;
  type?: string | null;
  brand?: string | null;
  model?: string | null;
  installDate?: string | null;
}

interface DeviceDto {
  id: string;
  name: string;
  type: string;
  brand?: string | null;
  model?: string | null;
  installDate?: string | null;
  houseId: string;
  createdAt: string;
}

interface DeviceSummaryDto {
  id: string;
  name: string;
  type: string;
  brand?: string | null;
  model?: string | null;
  installDate?: string | null;
  score: number;
  pendingCount: number;
  overdueCount: number;
}

interface MaintenanceTypeWithStatusDto {
  id: string;
  name: string;
  periodicity: string;
  customDays?: number | null;
  deviceId: string;
  createdAt: string;
  status: 'up_to_date' | 'pending' | 'overdue';
  lastMaintenanceDate?: string | null;
  nextDueDate?: string | null;
}

interface DeviceDetailDto {
  id: string;
  name: string;
  type: string;
  brand?: string | null;
  model?: string | null;
  installDate?: string | null;
  houseId: string;
  houseName?: string;
  createdAt: string;
  score: number;
  status: string;
  pendingCount: number;
  maintenanceTypesCount: number;
  maintenanceTypes: MaintenanceTypeWithStatusDto[];
  totalSpent: number;
  maintenanceCount: number;
}

/**
 * Hook to fetch all devices for a house
 */
export function useDevices(
  houseId: string,
  options?: UseQueryOptions<DeviceSummaryDto[], Error>
) {
  return useQuery({
    queryKey: ['houses', houseId, 'devices'],
    queryFn: async () => {
      const response = await apiClient.get<DeviceSummaryDto[]>(`/api/v1/houses/${houseId}/devices`);
      return response.data;
    },
    enabled: !!houseId,
    ...options,
  });
}

/**
 * Hook to fetch a single device with details
 */
export function useDevice(
  deviceId: string,
  options?: UseQueryOptions<DeviceDetailDto, Error>
) {
  return useQuery({
    queryKey: ['devices', deviceId],
    queryFn: async () => {
      const response = await apiClient.get<DeviceDetailDto>(`/api/v1/devices/${deviceId}`);
      return response.data;
    },
    enabled: !!deviceId,
    ...options,
  });
}

/**
 * Hook to create a new device
 */
export function useCreateDevice(
  houseId: string,
  options?: UseMutationOptions<DeviceDto, Error, CreateDeviceRequestDto>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: CreateDeviceRequestDto) => {
      const response = await apiClient.post<DeviceDto>(`/api/v1/houses/${houseId}/devices`, data);
      return response.data;
    },
    onSuccess: async () => {
      // Force refetch and wait for it to complete
      await queryClient.invalidateQueries({
        queryKey: ['houses', houseId, 'devices'],
        refetchType: 'active'
      });
      await queryClient.invalidateQueries({
        queryKey: ['houses', houseId],
        refetchType: 'active'
      });
    },
    ...options,
  });
}

/**
 * Hook to update a device
 */
export function useUpdateDevice(
  deviceId: string,
  options?: UseMutationOptions<DeviceDto, Error, UpdateDeviceRequestDto>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: UpdateDeviceRequestDto) => {
      const response = await apiClient.put<DeviceDto>(`/api/v1/devices/${deviceId}`, data);
      return response.data;
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ['devices', deviceId],
        refetchType: 'active'
      });
    },
    ...options,
  });
}

/**
 * Hook to delete a device
 */
export function useDeleteDevice(
  options?: UseMutationOptions<void, Error, { deviceId: string; houseId: string }>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ deviceId }) => {
      await apiClient.delete(`/api/v1/devices/${deviceId}`);
    },
    onSuccess: async (_, { houseId }) => {
      await queryClient.invalidateQueries({
        queryKey: ['houses', houseId, 'devices'],
        refetchType: 'active'
      });
      await queryClient.invalidateQueries({
        queryKey: ['houses', houseId],
        refetchType: 'active'
      });
    },
    ...options,
  });
}
