import { useMutation } from '@tanstack/react-query';
import apiClient from '../client';
import { useAuth } from '@/lib/auth/context';

// DTOs matching backend
interface RegisterRequestDto {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
}

interface LoginRequestDto {
  email: string;
  password: string;
}

interface UserDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  theme: string;
  language: string;
}

interface AuthResponseDto {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  user: UserDto;
}

// Extended response with first house ID for redirect after registration
interface RegisterResponseDto extends AuthResponseDto {
  firstHouseId?: string;
}

interface UseRegisterOptions {
  onSuccess?: (data: RegisterResponseDto) => void;
  onError?: (error: Error) => void;
}

interface UseLoginOptions {
  onSuccess?: (data: AuthResponseDto) => void;
  onError?: (error: Error) => void;
}

/**
 * Hook for user registration
 */
export function useRegister(options?: UseRegisterOptions) {
  const { login } = useAuth();

  return useMutation({
    mutationFn: async (data: RegisterRequestDto) => {
      const response = await apiClient.post<AuthResponseDto>('/api/v1/auth/register', data);

      // Get the first house ID for redirect
      let firstHouseId: string | undefined;
      try {
        const housesResponse = await apiClient.get<{ houses: { id: string }[] }>('/api/v1/houses', {
          headers: { Authorization: `Bearer ${response.data.accessToken}` }
        });
        firstHouseId = housesResponse.data.houses[0]?.id;
      } catch {
        // Ignore errors fetching houses
      }

      return { ...response.data, firstHouseId };
    },
    onSuccess: (data) => {
      // Always call login first
      login(data.accessToken, data.user);
      // Then call the user's onSuccess if provided
      options?.onSuccess?.(data);
    },
    onError: options?.onError,
  });
}

/**
 * Hook for user login
 */
export function useLogin(options?: UseLoginOptions) {
  const { login } = useAuth();

  return useMutation({
    mutationFn: async (data: LoginRequestDto) => {
      const response = await apiClient.post<AuthResponseDto>('/api/v1/auth/login', data);
      return response.data;
    },
    onSuccess: (data) => {
      // Always call login first
      login(data.accessToken, data.user);
      // Then call the user's onSuccess if provided
      options?.onSuccess?.(data);
    },
    onError: options?.onError,
  });
}
