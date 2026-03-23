import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createWrapper } from '@/__tests__/test-utils';
import {
  useMaintenanceTypes,
  useCreateMaintenanceType,
  useMaintenanceHistory,
  useUpcomingTasks,
  useLogMaintenance,
} from '../maintenance';

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

describe('useMaintenanceTypes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches maintenance types for a device', async () => {
    const mockTypes = [
      {
        id: 'mt1',
        name: 'Révision annuelle',
        periodicity: 'Annual',
        deviceId: 'd1',
        createdAt: '2024-01-01',
        status: 'up_to_date',
        lastMaintenanceDate: '2024-06-01',
        nextDueDate: '2025-06-01',
      },
    ];
    mockedClient.get.mockResolvedValueOnce({ data: mockTypes });

    const { result } = renderHook(() => useMaintenanceTypes('d1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockTypes);
    expect(mockedClient.get).toHaveBeenCalledWith('/api/v1/devices/d1/maintenance-types');
  });

  it('does not fetch when deviceId is empty', () => {
    renderHook(() => useMaintenanceTypes(''), { wrapper: createWrapper() });
    expect(mockedClient.get).not.toHaveBeenCalled();
  });
});

describe('useCreateMaintenanceType', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('creates a maintenance type', async () => {
    const newType = { id: 'mt2', name: 'Nettoyage', periodicity: 'Monthly', deviceId: 'd1', createdAt: '2024-01-01' };
    mockedClient.post.mockResolvedValueOnce({ data: newType });

    const { result } = renderHook(() => useCreateMaintenanceType('d1'), { wrapper: createWrapper() });

    result.current.mutate({ name: 'Nettoyage', periodicity: 'Monthly' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(newType);
    expect(mockedClient.post).toHaveBeenCalledWith('/api/v1/devices/d1/maintenance-types', {
      name: 'Nettoyage',
      periodicity: 'Monthly',
    });
  });
});

describe('useMaintenanceHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches maintenance history for a device', async () => {
    const mockHistory = {
      instances: [
        {
          id: 'mi1',
          date: '2024-06-01',
          cost: 150,
          provider: 'Technicien',
          maintenanceTypeId: 'mt1',
          maintenanceTypeName: 'Révision',
          createdAt: '2024-06-01',
        },
      ],
      totalSpent: 150,
      count: 1,
    };
    mockedClient.get.mockResolvedValueOnce({ data: mockHistory });

    const { result } = renderHook(() => useMaintenanceHistory('d1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockHistory);
    expect(mockedClient.get).toHaveBeenCalledWith('/api/v1/devices/d1/maintenance-history');
  });

  it('does not fetch when deviceId is empty', () => {
    renderHook(() => useMaintenanceHistory(''), { wrapper: createWrapper() });
    expect(mockedClient.get).not.toHaveBeenCalled();
  });
});

describe('useUpcomingTasks', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches upcoming tasks', async () => {
    const mockTasks = {
      tasks: [
        {
          maintenanceTypeId: 'mt1',
          maintenanceTypeName: 'Révision',
          deviceId: 'd1',
          deviceName: 'Chaudière',
          deviceType: 'Chaudière Gaz',
          houseId: 'h1',
          houseName: 'Maison',
          status: 'pending',
          nextDueDate: '2025-01-01',
          periodicity: 'Annual',
        },
      ],
      overdueCount: 0,
      pendingCount: 1,
    };
    mockedClient.get.mockResolvedValueOnce({ data: mockTasks });

    const { result } = renderHook(() => useUpcomingTasks(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockTasks);
    expect(mockedClient.get).toHaveBeenCalledWith('/api/v1/upcoming-tasks');
  });

  it('passes limit parameter', async () => {
    mockedClient.get.mockResolvedValueOnce({ data: { tasks: [], overdueCount: 0, pendingCount: 0 } });

    const { result } = renderHook(() => useUpcomingTasks(5), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockedClient.get).toHaveBeenCalledWith('/api/v1/upcoming-tasks?limit=5');
  });
});

describe('useLogMaintenance', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('logs a maintenance instance', async () => {
    const logged = {
      id: 'mi2',
      date: '2024-12-01T00:00:00Z',
      cost: 200,
      maintenanceTypeId: 'mt1',
      maintenanceTypeName: 'Révision',
      createdAt: '2024-12-01',
    };
    mockedClient.post.mockResolvedValueOnce({ data: logged });

    const { result } = renderHook(() => useLogMaintenance('mt1'), { wrapper: createWrapper() });

    result.current.mutate({ date: '2024-12-01T00:00:00Z', cost: 200 });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(logged);
    expect(mockedClient.post).toHaveBeenCalledWith('/api/v1/maintenance-types/mt1/instances', {
      date: '2024-12-01T00:00:00Z',
      cost: 200,
    });
  });
});
