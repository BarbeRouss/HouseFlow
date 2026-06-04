import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// vi.mock is hoisted, so we cannot reference variables declared above.
// Instead, we use vi.hoisted to create mock values before hoisting.
const { mockAxiosInstance, mockInterceptorsResponse, mockInterceptorsRequest } = vi.hoisted(() => {
  const mockInterceptorsResponse = { use: vi.fn() };
  const mockInterceptorsRequest = { use: vi.fn() };

  const mockAxiosInstance = vi.fn();
  (mockAxiosInstance as any).interceptors = {
    request: mockInterceptorsRequest,
    response: mockInterceptorsResponse,
  };
  (mockAxiosInstance as any).defaults = { headers: { common: {} } };

  return { mockAxiosInstance, mockInterceptorsResponse, mockInterceptorsRequest };
});

vi.mock('axios', () => ({
  default: {
    create: vi.fn(() => mockAxiosInstance),
    post: vi.fn(),
  },
  AxiosError: class extends Error {
    response: any;
    config: any;
    code?: string;
    constructor(message?: string, code?: string, config?: any, request?: any, response?: any) {
      super(message);
      this.code = code;
      this.config = config;
      this.response = response;
    }
    isAxiosError = true;
  },
  AxiosHeaders: class {
    private headers: Record<string, string> = {};
    set(key: string, value: string) { this.headers[key] = value; }
    get(key: string) { return this.headers[key]; }
  },
}));

// Import after mocking
import { onRetryStateChange } from '../client';

describe('Retry logic - module setup', () => {
  it('registers a response interceptor', () => {
    expect(mockInterceptorsResponse.use).toHaveBeenCalledTimes(1);
    const [successHandler, errorHandler] = mockInterceptorsResponse.use.mock.calls[0];
    expect(typeof successHandler).toBe('function');
    expect(typeof errorHandler).toBe('function');
  });

  it('registers a request interceptor', () => {
    expect(mockInterceptorsRequest.use).toHaveBeenCalledTimes(1);
  });
});

describe('onRetryStateChange', () => {
  it('is exported as a function', () => {
    expect(typeof onRetryStateChange).toBe('function');
  });

  it('returns an unsubscribe function', () => {
    const listener = vi.fn();
    const unsubscribe = onRetryStateChange(listener);
    expect(typeof unsubscribe).toBe('function');
    unsubscribe();
  });

  it('unsubscribe removes the listener', () => {
    const listener = vi.fn();
    const unsubscribe = onRetryStateChange(listener);
    unsubscribe();

    // Subscribing and unsubscribing should not throw
    const listener2 = vi.fn();
    const unsub2 = onRetryStateChange(listener2);
    unsub2();
  });
});

describe('Response interceptor error handler', () => {
  let errorHandler: (error: any) => Promise<any>;

  beforeEach(() => {
    errorHandler = mockInterceptorsResponse.use.mock.calls[0][1];
  });

  it('rejects 4xx errors without retrying', async () => {
    const error = {
      response: { status: 400, data: { error: 'Bad Request' } },
      config: { method: 'get', url: '/api/test', headers: {} },
      isAxiosError: true,
    };

    await expect(errorHandler(error)).rejects.toBeDefined();
  });

  it('rejects non-retryable methods (POST) even for 5xx errors', async () => {
    const error = {
      response: { status: 500, data: {} },
      config: { method: 'post', url: '/api/test', headers: {} },
      isAxiosError: true,
    };

    await expect(errorHandler(error)).rejects.toBeDefined();
  });

  it('rejects 401 errors for auth endpoints', async () => {
    const error = {
      response: { status: 401, data: {} },
      config: { method: 'get', url: '/api/v1/auth/login', headers: {}, _retry: false },
      isAxiosError: true,
    };

    await expect(errorHandler(error)).rejects.toBeDefined();
  });

  it('attempts retry for GET on 5xx errors', async () => {
    const error = {
      response: { status: 503, data: {} },
      config: { method: 'get', url: '/api/test', headers: {} },
      isAxiosError: true,
    };

    // The retry will call apiClient (mockAxiosInstance), which we can make succeed
    mockAxiosInstance.mockResolvedValueOnce({ data: 'ok', status: 200 });

    const result = await errorHandler(error);
    expect(result).toEqual({ data: 'ok', status: 200 });
    expect(mockAxiosInstance).toHaveBeenCalled();
  });

  it('attempts retry for DELETE on network errors', async () => {
    const error = {
      response: undefined, // network error = no response
      config: { method: 'delete', url: '/api/test', headers: {} },
      isAxiosError: true,
    };

    mockAxiosInstance.mockResolvedValueOnce({ data: 'deleted', status: 204 });

    const result = await errorHandler(error);
    expect(result).toEqual({ data: 'deleted', status: 204 });
  });

  it('notifies retry listeners during retry', async () => {
    const listener = vi.fn();
    const unsubscribe = onRetryStateChange(listener);

    const error = {
      response: { status: 500, data: {} },
      config: { method: 'get', url: '/api/test', headers: {} },
      isAxiosError: true,
    };

    mockAxiosInstance.mockResolvedValueOnce({ data: 'ok', status: 200 });

    await errorHandler(error);

    // Listener should have been called with true (retrying) and then false (done)
    expect(listener).toHaveBeenCalledWith(true);
    expect(listener).toHaveBeenCalledWith(false);

    unsubscribe();
  });

  it('stops retrying after max attempts', async () => {
    const error = {
      response: { status: 500, data: {} },
      config: { method: 'get', url: '/api/test', headers: {}, _retryCount: 3 },
      isAxiosError: true,
    };

    // Already at max retries, should reject immediately
    await expect(errorHandler(error)).rejects.toBeDefined();
  });
});
