import { useQuery, useMutation, useQueryClient, UseQueryOptions, UseMutationOptions } from '@tanstack/react-query';
import apiClient from '../client';

// DTOs matching backend MVP model
interface CreateHouseRequestDto {
  name: string;
  address?: string | null;
  zipCode?: string | null;
  city?: string | null;
}

interface UpdateHouseRequestDto {
  name?: string | null;
  address?: string | null;
  zipCode?: string | null;
  city?: string | null;
}

interface HouseDto {
  id: string;
  name: string;
  address?: string | null;
  zipCode?: string | null;
  city?: string | null;
  createdAt?: string;
}

interface HouseSummaryDto {
  id: string;
  name: string;
  address?: string | null;
  zipCode?: string | null;
  city?: string | null;
  createdAt?: string;
  score: number;
  devicesCount: number;
  pendingCount: number;
  overdueCount: number;
  userRole?: string | null;
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

interface HouseDetailDto extends HouseSummaryDto {
  devices: DeviceSummaryDto[];
  userRole?: string | null;
}

interface HousesListResponseDto {
  houses: HouseSummaryDto[];
  globalScore: number;
}

/**
 * Hook to fetch all houses for current user with scores
 */
export function useHouses(options?: UseQueryOptions<HousesListResponseDto, Error>) {
  return useQuery({
    queryKey: ['houses'],
    queryFn: async () => {
      const response = await apiClient.get<HousesListResponseDto>('/api/v1/houses');
      return response.data;
    },
    ...options,
  });
}

/**
 * Hook to fetch a single house with details and devices
 */
export function useHouse(houseId: string, options?: UseQueryOptions<HouseDetailDto, Error>) {
  return useQuery({
    queryKey: ['houses', houseId],
    queryFn: async () => {
      const response = await apiClient.get<HouseDetailDto>(`/api/v1/houses/${houseId}`);
      return response.data;
    },
    enabled: !!houseId,
    ...options,
  });
}

/**
 * Hook to create a new house (with optimistic update)
 */
export function useCreateHouse(
  options?: UseMutationOptions<HouseDto, Error, CreateHouseRequestDto>
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateHouseRequestDto) => {
      const response = await apiClient.post<HouseDto>('/api/v1/houses', data);
      return response.data;
    },
    onMutate: async (newHouse) => {
      await queryClient.cancelQueries({ queryKey: ['houses'] });
      const previousData = queryClient.getQueryData<HousesListResponseDto>(['houses']);

      if (previousData) {
        const optimisticHouse: HouseSummaryDto = {
          id: `temp-${Date.now()}`,
          name: newHouse.name,
          address: newHouse.address,
          zipCode: newHouse.zipCode,
          city: newHouse.city,
          score: 100,
          devicesCount: 0,
          pendingCount: 0,
          overdueCount: 0,
        };
        queryClient.setQueryData<HousesListResponseDto>(['houses'], {
          ...previousData,
          houses: [...previousData.houses, optimisticHouse],
        });
      }

      return { previousData };
    },
    onSuccess: (...args) => {
      options?.onSuccess?.(...args);
    },
    onError: (...args) => {
      const context = args[2];
      if (context?.previousData) {
        queryClient.setQueryData(['houses'], context.previousData);
      }
      options?.onError?.(...args);
    },
    onSettled: (...args) => {
      queryClient.invalidateQueries({ queryKey: ['houses'], refetchType: 'active' });
      options?.onSettled?.(...args);
    },
  });
}

/**
 * Hook to update a house
 */
export function useUpdateHouse(
  houseId: string,
  options?: UseMutationOptions<HouseDto, Error, UpdateHouseRequestDto>
) {
  const queryClient = useQueryClient();
  const { onSuccess, ...restOptions } = options || {};

  return useMutation({
    mutationFn: async (data: UpdateHouseRequestDto) => {
      const response = await apiClient.put<HouseDto>(`/api/v1/houses/${houseId}`, data);
      return response.data;
    },
    onSuccess: (data, variables, onMutateResult, context) => {
      queryClient.invalidateQueries({
        queryKey: ['houses'],
        refetchType: 'active'
      });
      queryClient.invalidateQueries({
        queryKey: ['houses', houseId],
        refetchType: 'active'
      });
      onSuccess?.(data, variables, onMutateResult, context);
    },
    ...restOptions,
  });
}

/**
 * Hook to delete a house (with optimistic update)
 */
export function useDeleteHouse(
  options?: UseMutationOptions<void, Error, string>
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (houseId: string) => {
      await apiClient.delete(`/api/v1/houses/${houseId}`);
    },
    onMutate: async (houseId) => {
      await queryClient.cancelQueries({ queryKey: ['houses'] });
      const previousData = queryClient.getQueryData<HousesListResponseDto>(['houses']);

      if (previousData) {
        queryClient.setQueryData<HousesListResponseDto>(['houses'], {
          ...previousData,
          houses: previousData.houses.filter((h) => h.id !== houseId),
        });
      }

      return { previousData };
    },
    onSuccess: (...args) => {
      options?.onSuccess?.(...args);
    },
    onError: (...args) => {
      const context = args[2];
      if (context?.previousData) {
        queryClient.setQueryData(['houses'], context.previousData);
      }
      options?.onError?.(...args);
    },
    onSettled: (...args) => {
      queryClient.invalidateQueries({ queryKey: ['houses'], refetchType: 'active' });
      options?.onSettled?.(...args);
    },
  });
}
