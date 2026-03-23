import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createWrapper } from '@/__tests__/test-utils';
import { useHouses, useHouse, useCreateHouse, useUpdateHouse, useDeleteHouse } from '../houses';

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
});
