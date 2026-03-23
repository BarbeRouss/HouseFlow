import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createWrapper } from '@/__tests__/test-utils';
import { useDevices, useDevice, useCreateDevice, useUpdateDevice, useDeleteDevice } from '../devices';

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
});
