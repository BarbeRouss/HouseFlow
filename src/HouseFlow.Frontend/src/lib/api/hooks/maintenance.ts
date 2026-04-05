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
  limit?: number,
  options?: UseQueryOptions<UpcomingTasksResponseDto, Error>
) {
  return useQuery({
    queryKey: ['upcoming-tasks', limit],
    queryFn: async () => {
      const params = limit ? `?limit=${limit}` : '';
      const response = await apiClient.get<UpcomingTasksResponseDto>(`/api/v1/upcoming-tasks${params}`);
      return response.data;
    },
    ...options,
  });
}

/**
 * Hook to log a maintenance instance (with optimistic update)
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
    onMutate: async (newLog) => {
      await queryClient.cancelQueries({ queryKey: ['devices'] });
      await queryClient.cancelQueries({ queryKey: ['upcoming-tasks'] });

      // Snapshot all device queries that contain this maintenance type
      const deviceQueries = queryClient.getQueriesData<MaintenanceTypeWithStatusDto[]>({
        queryKey: ['devices'],
        exact: false,
      });

      // Optimistically update the maintenance type status to up_to_date
      for (const [queryKey, data] of deviceQueries) {
        if (!Array.isArray(data)) continue;
        const hasType = data.some((mt) => mt.id === maintenanceTypeId);
        if (hasType) {
          queryClient.setQueryData<MaintenanceTypeWithStatusDto[]>(
            queryKey,
            data.map((mt) =>
              mt.id === maintenanceTypeId
                ? { ...mt, status: 'up_to_date' as const, lastMaintenanceDate: newLog.date }
                : mt
            )
          );
        }
      }

      // Optimistically remove the task from upcoming-tasks
      const upcomingQueries = queryClient.getQueriesData<UpcomingTasksResponseDto>({
        queryKey: ['upcoming-tasks'],
        exact: false,
      });
      for (const [queryKey, data] of upcomingQueries) {
        if (!data) continue;
        const filteredTasks = data.tasks.filter((t) => t.maintenanceTypeId !== maintenanceTypeId);
        queryClient.setQueryData<UpcomingTasksResponseDto>(queryKey, {
          tasks: filteredTasks,
          overdueCount: filteredTasks.filter((t) => t.status === 'overdue').length,
          pendingCount: filteredTasks.filter((t) => t.status === 'pending').length,
        });
      }

      return { deviceQueries, upcomingQueries };
    },
    onSuccess: (...args) => {
      options?.onSuccess?.(...args);
    },
    onError: (...args) => {
      const context = args[2];
      if (context?.deviceQueries) {
        for (const [queryKey, data] of context.deviceQueries) {
          queryClient.setQueryData(queryKey, data);
        }
      }
      if (context?.upcomingQueries) {
        for (const [queryKey, data] of context.upcomingQueries) {
          queryClient.setQueryData(queryKey, data);
        }
      }
      options?.onError?.(...args);
    },
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: ['devices'], refetchType: 'active' });
      await queryClient.invalidateQueries({ queryKey: ['houses'], refetchType: 'active' });
      await queryClient.invalidateQueries({ queryKey: ['upcoming-tasks'], refetchType: 'active' });
    },
  });
}
