import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../client';
import { useAuth } from '@/lib/auth/context';

export interface ApiKeyDto {
  id: string;
  name: string;
  prefix: string;
  scope: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface CreateApiKeyRequest {
  name: string;
  scope: string;
}

export interface CreateApiKeyResponse {
  id: string;
  name: string;
  key: string;
  prefix: string;
  scope: string;
  createdAt: string;
}

export function useApiKeys() {
  const { isAuthenticated } = useAuth();

  return useQuery<ApiKeyDto[]>({
    queryKey: ['apiKeys'],
    queryFn: async () => {
      const response = await apiClient.get<ApiKeyDto[]>('/api/v1/users/api-keys');
      return response.data;
    },
    enabled: isAuthenticated,
  });
}

export function useCreateApiKey() {
  const queryClient = useQueryClient();

  return useMutation<CreateApiKeyResponse, Error, CreateApiKeyRequest>({
    mutationFn: async (data) => {
      const response = await apiClient.post<CreateApiKeyResponse>('/api/v1/users/api-keys', data);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apiKeys'] });
    },
  });
}

export function useRevokeApiKey() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, string>({
    mutationFn: async (id) => {
      await apiClient.delete(`/api/v1/users/api-keys/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apiKeys'] });
    },
  });
}
