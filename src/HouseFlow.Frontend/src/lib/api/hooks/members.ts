import { useQuery, useMutation, useQueryClient, UseQueryOptions, UseMutationOptions } from '@tanstack/react-query';
import apiClient from '../client';

// DTOs matching backend collaboration model
export interface HouseMemberDto {
  id: string;
  userId: string;
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  canLogMaintenance: boolean;
  joinedAt: string;
}

export interface InvitationDto {
  id: string;
  token: string;
  role: string;
  status: string;
  expiresAt: string;
  createdAt: string;
}

export interface InvitationInfoDto {
  houseName: string;
  role: string;
  invitedBy: string;
  expiresAt: string;
  isExpired: boolean;
}

export interface AcceptInvitationResponseDto {
  houseId: string;
  houseName: string;
  role: string;
}

export interface HouseCollaboratorsDto {
  houseId: string;
  houseName: string;
  members: HouseMemberDto[];
}

export interface AllCollaboratorsResponseDto {
  houses: HouseCollaboratorsDto[];
}

interface CreateInvitationRequestDto {
  role: string;
}

interface UpdateMemberRoleRequestDto {
  role: string;
}

interface UpdateMemberPermissionsRequestDto {
  canLogMaintenance: boolean;
}

/**
 * Hook to fetch all collaborators across all owned houses
 */
export function useAllCollaborators(options?: UseQueryOptions<AllCollaboratorsResponseDto, Error>) {
  return useQuery({
    queryKey: ['collaborators'],
    queryFn: async () => {
      const response = await apiClient.get<AllCollaboratorsResponseDto>('/api/v1/collaborators');
      return response.data;
    },
    ...options,
  });
}

/**
 * Hook to fetch members of a specific house
 */
export function useHouseMembers(houseId: string, options?: UseQueryOptions<HouseMemberDto[], Error>) {
  return useQuery({
    queryKey: ['houses', houseId, 'members'],
    queryFn: async () => {
      const response = await apiClient.get<HouseMemberDto[]>(`/api/v1/houses/${houseId}/members`);
      return response.data;
    },
    enabled: !!houseId,
    ...options,
  });
}

/**
 * Hook to fetch invitations for a house
 */
export function useHouseInvitations(houseId: string, options?: UseQueryOptions<InvitationDto[], Error>) {
  return useQuery({
    queryKey: ['houses', houseId, 'invitations'],
    queryFn: async () => {
      const response = await apiClient.get<InvitationDto[]>(`/api/v1/houses/${houseId}/invitations`);
      return response.data;
    },
    enabled: !!houseId,
    ...options,
  });
}

/**
 * Hook to get invitation info by token (public, no auth required)
 */
export function useInvitationInfo(token: string, options?: UseQueryOptions<InvitationInfoDto, Error>) {
  return useQuery({
    queryKey: ['invitations', token],
    queryFn: async () => {
      const response = await apiClient.get<InvitationInfoDto>(`/api/v1/invitations/${token}`);
      return response.data;
    },
    enabled: !!token,
    ...options,
  });
}

/**
 * Hook to create an invitation for a house
 */
export function useCreateInvitation(
  houseId: string,
  options?: UseMutationOptions<InvitationDto, Error, CreateInvitationRequestDto>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: CreateInvitationRequestDto) => {
      const response = await apiClient.post<InvitationDto>(`/api/v1/houses/${houseId}/invitations`, data);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['houses', houseId, 'invitations'], refetchType: 'active' });
    },
    ...options,
  });
}

/**
 * Hook to accept an invitation
 */
export function useAcceptInvitation(
  options?: UseMutationOptions<AcceptInvitationResponseDto, Error, string>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (token: string) => {
      const response = await apiClient.post<AcceptInvitationResponseDto>(`/api/v1/invitations/${token}/accept`);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['houses'], refetchType: 'active' });
      queryClient.invalidateQueries({ queryKey: ['collaborators'], refetchType: 'active' });
    },
    ...options,
  });
}

/**
 * Hook to revoke an invitation
 */
export function useRevokeInvitation(
  houseId: string,
  options?: UseMutationOptions<void, Error, string>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (invitationId: string) => {
      await apiClient.delete(`/api/v1/invitations/${invitationId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['houses', houseId, 'invitations'], refetchType: 'active' });
    },
    ...options,
  });
}

/**
 * Hook to update a member's role
 */
export function useUpdateMemberRole(
  options?: UseMutationOptions<HouseMemberDto, Error, { memberId: string; role: string }>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ memberId, role }: { memberId: string; role: string }) => {
      const response = await apiClient.put<HouseMemberDto>(`/api/v1/members/${memberId}/role`, { role });
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['collaborators'], refetchType: 'active' });
      queryClient.invalidateQueries({ queryKey: ['houses'], refetchType: 'active' });
    },
    ...options,
  });
}

/**
 * Hook to update a member's permissions (canLogMaintenance for tenants)
 */
export function useUpdateMemberPermissions(
  options?: UseMutationOptions<void, Error, { memberId: string; canLogMaintenance: boolean }>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ memberId, canLogMaintenance }: { memberId: string; canLogMaintenance: boolean }) => {
      await apiClient.put(`/api/v1/members/${memberId}/permissions`, { canLogMaintenance });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['collaborators'], refetchType: 'active' });
      queryClient.invalidateQueries({ queryKey: ['houses'], refetchType: 'active' });
    },
    ...options,
  });
}

/**
 * Hook to remove a member from a house
 */
export function useRemoveMember(
  options?: UseMutationOptions<void, Error, string>
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (memberId: string) => {
      await apiClient.delete(`/api/v1/members/${memberId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['collaborators'], refetchType: 'active' });
      queryClient.invalidateQueries({ queryKey: ['houses'], refetchType: 'active' });
    },
    ...options,
  });
}
