import { useQuery, useMutation, useQueryClient, UseQueryOptions, UseMutationOptions } from '@tanstack/react-query';
import apiClient from '../client';

// Enums matching backend MVP model
type Periodicity = 'Annual' | 'Semestrial' | 'Quarterly' | 'Monthly' | 'Custom';

// DTOs matching backend MVP model
interface CreateMaintenanceTypeRequestDto {
  name: string;
  periodicity: Periodicity;
  customDays?: number | null;
}

interface UpdateMaintenanceTypeRequestDto {
  name?: string | null;
  periodicity?: Periodicity | null;
  customDays?: number | null;
}

interface MaintenanceTypeDto {
  id: string;
  name: string;
  periodicity: Periodicity;
  customDays?: number | null;
  deviceId: string;
  createdAt: string;
}

interface MaintenanceTypeWithStatusDto extends MaintenanceTypeDto {
  status: 'up_to_date' | 'pending' | 'overdue';
  lastMaintenanceDate?: string | null;
  nextDueDate?: string | null;
}

interface LogMaintenanceRequestDto {
  date: string;
  cost?: number | null;
  provider?: string | null;
  notes?: string | null;
}

interface MaintenanceInstanceDto {
  id: string;
  date: string;
  cost?: number | null;
  provider?: string | null;
  notes?: string | null;
  maintenanceTypeId: string;
  maintenanceTypeName: string;
  createdAt: string;
}

interface MaintenanceHistoryResponseDto {
  instances: MaintenanceInstanceDto[];
  totalSpent: number;
  count: number;
}

/**
 * Hook to fetch all maintenance types with status for a device
 */
export function useMaintenanceTypes(
  deviceId: string,
  options?: UseQueryOptions<MaintenanceTypeWithStatusDto[], Error>
) {
  return useQuery({
    queryKey: ['devices', deviceId, 'maintenance-types'],
    queryFn: async () => {
      const response = await apiClient.get<MaintenanceTypeWithStatusDto[]>(`/api/v1/devices/${deviceId}/maintenance-types`);
      return response.data;
    },
    enabled: !!deviceId,
    ...options,
  });
}

/**
 * Hook to create a maintenance type
 */
export function useCreateMaintenanceType(
  deviceId: string,
  options?: UseMutationOptions<MaintenanceTypeDto, Error, CreateMaintenanceTypeRequestDto>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: CreateMaintenanceTypeRequestDto) => {
      const response = await apiClient.post<MaintenanceTypeDto>(
        `/api/v1/devices/${deviceId}/maintenance-types`,
        data
      );
      return response.data;
    },
    ...options,
    onSuccess: async (data, variables, onMutateResult, context) => {
      // First invalidate queries and wait for refetch
      await queryClient.invalidateQueries({
        queryKey: ['devices', deviceId, 'maintenance-types'],
        refetchType: 'active'
      });
      await queryClient.invalidateQueries({
        queryKey: ['devices', deviceId],
        refetchType: 'active'
      });
      // Then call user's onSuccess if provided
      await options?.onSuccess?.(data, variables, onMutateResult, context);
    },
  });
}

/**
 * Hook to update a maintenance type
 */
export function useUpdateMaintenanceType(
  typeId: string,
  options?: UseMutationOptions<MaintenanceTypeDto, Error, UpdateMaintenanceTypeRequestDto>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: UpdateMaintenanceTypeRequestDto) => {
      const response = await apiClient.put<MaintenanceTypeDto>(
        `/api/v1/maintenance-types/${typeId}`,
        data
      );
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ['devices'],
        refetchType: 'active'
      });
    },
    ...options,
  });
}

/**
 * Hook to delete a maintenance type
 */
export function useDeleteMaintenanceType(
  options?: UseMutationOptions<void, Error, string>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (typeId: string) => {
      await apiClient.delete(`/api/v1/maintenance-types/${typeId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ['devices'],
        refetchType: 'active'
      });
    },
    ...options,
  });
}

/**
 * Hook to fetch maintenance history for a device
 */
export function useMaintenanceHistory(
  deviceId: string,
  options?: UseQueryOptions<MaintenanceHistoryResponseDto, Error>
) {
  return useQuery({
    queryKey: ['devices', deviceId, 'maintenance-history'],
    queryFn: async () => {
      const response = await apiClient.get<MaintenanceHistoryResponseDto>(
        `/api/v1/devices/${deviceId}/maintenance-history`
      );
      return response.data;
    },
    enabled: !!deviceId,
    ...options,
  });
}

// ============================================================================
// UPCOMING TASKS
// ============================================================================

export interface UpcomingTaskDto {
  maintenanceTypeId: string;
  maintenanceTypeName: string;
  deviceId: string;
  deviceName: string;
  deviceType: string;
  houseId: string;
  houseName: string;
  status: 'pending' | 'overdue';
  nextDueDate?: string | null;
  lastMaintenanceDate?: string | null;
  periodicity: string;
}

export interface UpcomingTasksResponseDto {
  tasks: UpcomingTaskDto[];
  overdueCount: number;
  pendingCount: number;
}

/**
 * Hook to fetch upcoming maintenance tasks across all houses/devices
 */
export function useUpcomingTasks(
  options?: UseQueryOptions<UpcomingTasksResponseDto, Error>
) {
  return useQuery({
    queryKey: ['upcoming-tasks'],
    queryFn: async () => {
      const response = await apiClient.get<UpcomingTasksResponseDto>('/api/v1/upcoming-tasks');
      return response.data;
    },
    ...options,
  });
}

/**
 * Hook to log a maintenance instance
 */
export function useLogMaintenance(
  maintenanceTypeId: string,
  options?: UseMutationOptions<MaintenanceInstanceDto, Error, LogMaintenanceRequestDto>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: LogMaintenanceRequestDto) => {
      const response = await apiClient.post<MaintenanceInstanceDto>(
        `/api/v1/maintenance-types/${maintenanceTypeId}/instances`,
        data
      );
      return response.data;
    },
    ...options,
    onSuccess: async (data, variables, onMutateResult, context) => {
      // First invalidate queries and wait for refetch
      await queryClient.invalidateQueries({
        queryKey: ['devices'],
        refetchType: 'active'
      });
      await queryClient.invalidateQueries({
        queryKey: ['houses'],
        refetchType: 'active'
      });
      await queryClient.invalidateQueries({
        queryKey: ['upcoming-tasks'],
        refetchType: 'active'
      });
      // Then call user's onSuccess if provided
      await options?.onSuccess?.(data, variables, onMutateResult, context);
    },
  });
}
