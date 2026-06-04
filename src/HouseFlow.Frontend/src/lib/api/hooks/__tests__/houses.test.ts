import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createWrapper, createTestQueryClient } from '@/__tests__/test-utils';
import { useHouses, useHouse, useCreateHouse, useUpdateHouse, useDeleteHouse } from '../houses';

function createWrapperWith(queryClient: QueryClient) {
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

// Mock the API client
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

describe('useHouses', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches houses list successfully', async () => {
    const mockData = {
      houses: [
        { id: '1', name: 'Maison', score: 85, devicesCount: 3, pendingCount: 1, overdueCount: 0 },
      ],
      globalScore: 85,
    };
    mockedClient.get.mockResolvedValueOnce({ data: mockData });

    const { result } = renderHook(() => useHouses(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockData);
    expect(mockedClient.get).toHaveBeenCalledWith('/api/v1/houses');
  });

  it('handles fetch error', async () => {
    mockedClient.get.mockRejectedValueOnce(new Error('Network error'));

    const { result } = renderHook(() => useHouses(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Network error');
  });
});

describe('useHouse', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches a single house by id', async () => {
    const mockHouse = {
      id: '1',
      name: 'Maison',
      score: 90,
      devicesCount: 2,
      pendingCount: 0,
      overdueCount: 0,
      devices: [],
    };
    mockedClient.get.mockResolvedValueOnce({ data: mockHouse });

    const { result } = renderHook(() => useHouse('1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockHouse);
    expect(mockedClient.get).toHaveBeenCalledWith('/api/v1/houses/1');
  });

  it('does not fetch when houseId is empty', () => {
    renderHook(() => useHouse(''), { wrapper: createWrapper() });
    expect(mockedClient.get).not.toHaveBeenCalled();
  });
});

describe('useCreateHouse', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('creates a house and returns data', async () => {
    const newHouse = { id: '2', name: 'Appartement' };
    mockedClient.post.mockResolvedValueOnce({ data: newHouse });

    const { result } = renderHook(() => useCreateHouse(), { wrapper: createWrapper() });

    result.current.mutate({ name: 'Appartement' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(newHouse);
    expect(mockedClient.post).toHaveBeenCalledWith('/api/v1/houses', { name: 'Appartement' });
  });

  it('optimistically adds house to cache and invalidates on success', async () => {
    const existingData = {
      houses: [{ id: '1', name: 'Maison', score: 85, devicesCount: 3, pendingCount: 1, overdueCount: 0 }],
      globalScore: 85,
    };
    const newHouse = { id: '2', name: 'Appartement' };
    mockedClient.post.mockResolvedValueOnce({ data: newHouse });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
    });
    queryClient.setQueryData(['houses'], existingData);
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useCreateHouse(), { wrapper });

    result.current.mutate({ name: 'Appartement' });

    // Optimistic update should add the house immediately
    await waitFor(() => {
      const data = queryClient.getQueryData<{ houses: { name: string }[] }>(['houses']);
      expect(data?.houses).toHaveLength(2);
      expect(data?.houses[1]?.name).toBe('Appartement');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['houses'], refetchType: 'active' });
  });

  it('rolls back optimistic update on error', async () => {
    const existingData = {
      houses: [{ id: '1', name: 'Maison', score: 85, devicesCount: 3, pendingCount: 1, overdueCount: 0 }],
      globalScore: 85,
    };
    mockedClient.post.mockRejectedValueOnce(new Error('Server error'));

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
    });
    queryClient.setQueryData(['houses'], existingData);
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useCreateHouse(), { wrapper });

    result.current.mutate({ name: 'Appartement' });

    await waitFor(() => expect(result.current.isError).toBe(true));

    // Cache should be rolled back to original data
    const data = queryClient.getQueryData(['houses']);
    expect(data).toEqual(existingData);
  });
});

describe('useUpdateHouse', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('updates a house', async () => {
    const updated = { id: '1', name: 'Maison Bleue' };
    mockedClient.put.mockResolvedValueOnce({ data: updated });

    const { result } = renderHook(() => useUpdateHouse('1'), { wrapper: createWrapper() });

    result.current.mutate({ name: 'Maison Bleue' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(updated);
    expect(mockedClient.put).toHaveBeenCalledWith('/api/v1/houses/1', { name: 'Maison Bleue' });
  });

  it('invalidates cache and calls onSuccess when both are provided', async () => {
    const updated = { id: '1', name: 'Maison Bleue' };
    mockedClient.put.mockResolvedValueOnce({ data: updated });
    const onSuccess = vi.fn();

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useUpdateHouse('1', { onSuccess }), { wrapper });

    result.current.mutate({ name: 'Maison Bleue' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['houses'], refetchType: 'active' });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['houses', '1'], refetchType: 'active' });
    expect(onSuccess).toHaveBeenCalled();
  });
});

describe('useDeleteHouse', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('deletes a house', async () => {
    mockedClient.delete.mockResolvedValueOnce({});

    const { result } = renderHook(() => useDeleteHouse(), { wrapper: createWrapper() });

    result.current.mutate('1');

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockedClient.delete).toHaveBeenCalledWith('/api/v1/houses/1');
  });

  it('optimistically removes house from cache and invalidates on success', async () => {
    const existingData = {
      houses: [
        { id: '1', name: 'Maison', score: 85, devicesCount: 3, pendingCount: 1, overdueCount: 0 },
        { id: '2', name: 'Appartement', score: 90, devicesCount: 1, pendingCount: 0, overdueCount: 0 },
      ],
      globalScore: 87,
    };
    mockedClient.delete.mockResolvedValueOnce({});

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
    });
    queryClient.setQueryData(['houses'], existingData);
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useDeleteHouse(), { wrapper });

    result.current.mutate('1');

    // Optimistic update should remove the house immediately
    await waitFor(() => {
      const data = queryClient.getQueryData<{ houses: { id: string }[] }>(['houses']);
      expect(data?.houses).toHaveLength(1);
      expect(data?.houses[0]?.id).toBe('2');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['houses'], refetchType: 'active' });
  });

  it('rolls back optimistic update on error', async () => {
    const existingData = {
      houses: [
        { id: '1', name: 'Maison', score: 85, devicesCount: 3, pendingCount: 1, overdueCount: 0 },
      ],
      globalScore: 85,
    };
    mockedClient.delete.mockRejectedValueOnce(new Error('Server error'));

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
    });
    queryClient.setQueryData(['houses'], existingData);
    const wrapper = createWrapperWith(queryClient);

    const { result } = renderHook(() => useDeleteHouse(), { wrapper });

    result.current.mutate('1');

    await waitFor(() => expect(result.current.isError).toBe(true));

    // Cache should be rolled back to original data
    const data = queryClient.getQueryData(['houses']);
    expect(data).toEqual(existingData);
  });
});
