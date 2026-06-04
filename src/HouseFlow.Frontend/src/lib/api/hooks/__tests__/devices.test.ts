import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createWrapper } from '@/__tests__/test-utils';
import { useDevices, useDevice, useCreateDevice, useUpdateDevice, useDeleteDevice } from '../devices';

function createWrapperWith(queryClient: QueryClient) {
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

vi.mock('../../client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

import apiClient from '../../client';

const mockedClient = vi.mocked(apiClient);

describe('useDevices', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches devices for a house', async () => {
    const mockDevices = [
      { id: 'd1', name: 'Chaudière', type: 'Chaudière Gaz', score: 100, pendingCount: 0, overdueCount: 0 },
    ];
    mockedClient.get.mockResolvedValueOnce({ data: mockDevices });

    const { result } = renderHook(() => useDevices('h1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockDevices);
    expect(mockedClient.get).toHaveBeenCalledWith('/api/v1/houses/h1/devices');
  });

  it('does not fetch when houseId is empty', () => {
    renderHook(() => useDevices(''), { wrapper: createWrapper() });
    expect(mockedClient.get).not.toHaveBeenCalled();
  });
});

describe('useDevice', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches a single device by id', async () => {
    const mockDevice = {
      id: 'd1',
      name: 'Chaudière',
      type: 'Chaudière Gaz',
      houseId: 'h1',
      createdAt: '2024-01-01',
      score: 100,
      status: 'up_to_date',
      pendingCount: 0,
      maintenanceTypesCount: 1,
      maintenanceTypes: [],
      totalSpent: 0,
      maintenanceCount: 0,
    };
    mockedClient.get.mockResolvedValueOnce({ data: mockDevice });

    const { result } = renderHook(() => useDevice('d1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockDevice);
    expect(mockedClient.get).toHaveBeenCalledWith('/api/v1/devices/d1');
  });

  it('does not fetch when deviceId is empty', () => {
    renderHook(() => useDevice(''), { wrapper: createWrapper() });
    expect(mockedClient.get).not.toHaveBeenCalled();
  });
});

describe('useCreateDevice', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('creates a device for a house', async () => {
    const newDevice = { id: 'd2', name: 'VMC', type: 'VMC', houseId: 'h1', createdAt: '2024-01-01' };
    mockedClient.post.mockResolvedValueOnce({ data: newDevice });

    const { result } = renderHook(() => useCreateDevice('h1'), { wrapper: createWrapper() });

    result.current.mutate({ name: 'VMC', type: 'VMC' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(newDevice);
    expect(mockedClient.post).toHaveBeenCalledWith('/api/v1/houses/h1/devices', { name: 'VMC', type: 'VMC' });
  });

  it('optimistically adds device to cache and invalidates on success', async () => {
    const existingDevices = [
      { id: 'd1', name: 'Chaudière', type: 'Chaudière Gaz', score: 100, pendingCount: 0, overdueCount: 0 },
    ];
    const newDevice = { id: 'd2', name: 'VMC', type: 'VMC', houseId: 'h1', createdAt: '2024-01-01' };
    mockedClient.post.mockResolvedValueOnce({ data: newDevice });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
    });
    queryClient.setQueryData(['houses', 'h1', 'devices'], existingDevices);
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useCreateDevice('h1'), { wrapper });

    result.current.mutate({ name: 'VMC', type: 'VMC' });

    // Optimistic update should add the device immediately
    await waitFor(() => {
      const data = queryClient.getQueryData<{ name: string }[]>(['houses', 'h1', 'devices']);
      expect(data).toHaveLength(2);
      expect(data?.[1]?.name).toBe('VMC');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['houses', 'h1', 'devices'], refetchType: 'active' });
  });

  it('rolls back optimistic update on error', async () => {
    const existingDevices = [
      { id: 'd1', name: 'Chaudière', type: 'Chaudière Gaz', score: 100, pendingCount: 0, overdueCount: 0 },
    ];
    mockedClient.post.mockRejectedValueOnce(new Error('Server error'));

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
    });
    queryClient.setQueryData(['houses', 'h1', 'devices'], existingDevices);
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useCreateDevice('h1'), { wrapper });

    result.current.mutate({ name: 'VMC', type: 'VMC' });

    await waitFor(() => expect(result.current.isError).toBe(true));

    const data = queryClient.getQueryData(['houses', 'h1', 'devices']);
    expect(data).toEqual(existingDevices);
  });
});

describe('useUpdateDevice', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('updates a device', async () => {
    const updated = { id: 'd1', name: 'Chaudière Mise à jour', type: 'Chaudière Gaz', houseId: 'h1', createdAt: '2024-01-01' };
    mockedClient.put.mockResolvedValueOnce({ data: updated });

    const { result } = renderHook(() => useUpdateDevice('d1'), { wrapper: createWrapper() });

    result.current.mutate({ name: 'Chaudière Mise à jour' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(updated);
    expect(mockedClient.put).toHaveBeenCalledWith('/api/v1/devices/d1', { name: 'Chaudière Mise à jour' });
  });
});

describe('useDeleteDevice', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('deletes a device', async () => {
    mockedClient.delete.mockResolvedValueOnce({});

    const { result } = renderHook(() => useDeleteDevice(), { wrapper: createWrapper() });

    result.current.mutate({ deviceId: 'd1', houseId: 'h1' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockedClient.delete).toHaveBeenCalledWith('/api/v1/devices/d1');
  });

  it('optimistically removes device from cache and invalidates on success', async () => {
    const existingDevices = [
      { id: 'd1', name: 'Chaudière', type: 'Chaudière Gaz', score: 100, pendingCount: 0, overdueCount: 0 },
      { id: 'd2', name: 'VMC', type: 'VMC', score: 90, pendingCount: 1, overdueCount: 0 },
    ];
    mockedClient.delete.mockResolvedValueOnce({});

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
    });
    queryClient.setQueryData(['houses', 'h1', 'devices'], existingDevices);
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useDeleteDevice(), { wrapper });

    result.current.mutate({ deviceId: 'd1', houseId: 'h1' });

    // Optimistic update should remove the device immediately
    await waitFor(() => {
      const data = queryClient.getQueryData<{ id: string }[]>(['houses', 'h1', 'devices']);
      expect(data).toHaveLength(1);
      expect(data?.[0]?.id).toBe('d2');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['houses', 'h1', 'devices'], refetchType: 'active' });
  });

  it('rolls back optimistic update on error', async () => {
    const existingDevices = [
      { id: 'd1', name: 'Chaudière', type: 'Chaudière Gaz', score: 100, pendingCount: 0, overdueCount: 0 },
    ];
    mockedClient.delete.mockRejectedValueOnce(new Error('Server error'));

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
    });
    queryClient.setQueryData(['houses', 'h1', 'devices'], existingDevices);
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useDeleteDevice(), { wrapper });

    result.current.mutate({ deviceId: 'd1', houseId: 'h1' });

    await waitFor(() => expect(result.current.isError).toBe(true));

    const data = queryClient.getQueryData(['houses', 'h1', 'devices']);
    expect(data).toEqual(existingDevices);
  });
});
