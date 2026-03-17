import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../client';
import { useAuth } from '@/lib/auth/context';

export interface UserSettingsDto {
  theme: string;
  language: string;
}

interface UpdateUserSettingsDto {
  theme: string;
  language: string;
}

export function useUserSettings() {
  const { isAuthenticated } = useAuth();

  return useQuery<UserSettingsDto>({
    queryKey: ['userSettings'],
    queryFn: async () => {
      const response = await apiClient.get<UserSettingsDto>('/api/v1/users/settings');
      return response.data;
    },
    staleTime: Infinity,
    enabled: isAuthenticated,
  });
}

export function useUpdateUserSettings() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: UpdateUserSettingsDto) => {
      const response = await apiClient.put<UserSettingsDto>('/api/v1/users/settings', data);
      return response.data;
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['userSettings'], data);
    },
  });
}
