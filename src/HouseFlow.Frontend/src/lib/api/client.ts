import axios, { AxiosError, AxiosResponse, InternalAxiosRequestConfig } from 'axios';

declare global {
  interface Window {
    __RUNTIME_CONFIG__?: { API_URL: string };
  }
}

// Get API URL: runtime config (injected by server layout) > env var > fallback
function getApiUrl(): string {
  if (typeof window !== 'undefined' && window.__RUNTIME_CONFIG__?.API_URL) {
    return window.__RUNTIME_CONFIG__.API_URL;
  }
  return process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5203';
}

const API_URL = getApiUrl();

// Token storage key
const ACCESS_TOKEN_KEY = 'houseflow_access_token';

// In-memory fallback for SSR
let memoryToken: string | null = null;
let isRefreshing = false;
let refreshSubscribers: ((token: string) => void)[] = [];

// Check for initial token from E2E tests (set via addInitScript)
if (typeof window !== 'undefined' && (window as any).__INITIAL_AUTH_TOKEN) {
  memoryToken = (window as any).__INITIAL_AUTH_TOKEN;
  localStorage.setItem(ACCESS_TOKEN_KEY, memoryToken!);
  delete (window as any).__INITIAL_AUTH_TOKEN; // Clean up after reading
}

/**
 * Set the access token (localStorage for persistence + memory for SSR)
 */
export function setAccessToken(token: string | null) {
  memoryToken = token;
  if (typeof window !== 'undefined') {
    if (token) {
      localStorage.setItem(ACCESS_TOKEN_KEY, token);
    } else {
      localStorage.removeItem(ACCESS_TOKEN_KEY);
    }
  }
}

/**
 * Get the current access token (from localStorage or memory)
 */
export function getAccessToken(): string | null {
  if (typeof window !== 'undefined') {
    // Try localStorage first (survives page refresh)
    const storedToken = localStorage.getItem(ACCESS_TOKEN_KEY);
    if (storedToken) {
      memoryToken = storedToken;
      return storedToken;
    }
  }
  return memoryToken;
}

// Expose setAccessToken globally for E2E tests
if (typeof window !== 'undefined') {
  (window as any).__setAccessToken = setAccessToken;
}

/**
 * Subscribe to token refresh events
 */
function subscribeTokenRefresh(callback: (token: string) => void) {
  refreshSubscribers.push(callback);
}

/**
 * Notify all subscribers when token is refreshed
 */
function onTokenRefreshed(token: string) {
  refreshSubscribers.forEach(callback => callback(token));
  refreshSubscribers = [];
}

/**
 * Refresh the access token using the refresh token (in HttpOnly cookie)
 */
async function refreshAccessToken(): Promise<string | null> {
  try {
    const response = await axios.post(
      `${API_URL}/api/v1/auth/refresh`,
      {},
      {
        withCredentials: true, // Important: send cookies
        headers: {
          'Content-Type': 'application/json',
        },
      }
    );

    const newToken = response.data.accessToken;
    setAccessToken(newToken);
    return newToken;
  } catch (error) {
    // Refresh failed - user needs to login again
    setAccessToken(null);
    if (typeof window !== 'undefined') {
      // Clear user data and redirect to login
      sessionStorage.removeItem('houseflow_auth_user');
      window.location.href = '/fr/login';
    }
    return null;
  }
}

// --- Retry configuration ---
const RETRY_MAX_ATTEMPTS = 3;
const RETRY_INITIAL_DELAY_MS = 100;
const RETRY_MAX_DELAY_MS = 2000;

// Methods safe to retry (idempotent)
const RETRYABLE_METHODS = new Set(['get', 'put', 'delete', 'head', 'options']);

// Retry state tracking for UI indicator
type RetryListener = (retrying: boolean) => void;
let retryListeners: RetryListener[] = [];
let activeRetries = 0;

export function onRetryStateChange(listener: RetryListener): () => void {
  retryListeners.push(listener);
  return () => {
    retryListeners = retryListeners.filter(l => l !== listener);
  };
}

function setRetrying(active: boolean) {
  activeRetries += active ? 1 : -1;
  activeRetries = Math.max(0, activeRetries);
  retryListeners.forEach(l => l(activeRetries > 0));
}

function isRetryableError(error: AxiosError): boolean {
  // Network errors (no response received)
  if (!error.response) return true;
  // Server errors (5xx)
  if (error.response.status >= 500) return true;
  // Request timeout
  if (error.code === 'ECONNABORTED') return true;
  return false;
}

function isRetryableMethod(config: InternalAxiosRequestConfig): boolean {
  return RETRYABLE_METHODS.has((config.method || '').toLowerCase());
}

function getRetryDelay(attempt: number): number {
  const exponentialDelay = RETRY_INITIAL_DELAY_MS * Math.pow(2, attempt);
  const delay = Math.min(exponentialDelay, RETRY_MAX_DELAY_MS);
  // Add jitter (±25%) to prevent thundering herd
  const jitter = delay * 0.25 * (Math.random() * 2 - 1);
  return delay + jitter;
}

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function retryRequest(
  error: AxiosError,
  config: InternalAxiosRequestConfig & { _retryCount?: number },
): Promise<AxiosResponse> {
  const attempt = config._retryCount || 0;

  if (attempt >= RETRY_MAX_ATTEMPTS || !isRetryableMethod(config) || !isRetryableError(error)) {
    throw error;
  }

  config._retryCount = attempt + 1;

  if (attempt === 0) setRetrying(true);

  const delay = getRetryDelay(attempt);
  await sleep(delay);

  try {
    const response = await apiClient(config);
    setRetrying(false);
    return response;
  } catch (retryError) {
    if (config._retryCount >= RETRY_MAX_ATTEMPTS) {
      setRetrying(false);
    }
    throw retryError;
  }
}

/**
 * Axios client configured for HouseFlow API
 * Uses HttpOnly cookies for refresh tokens and in-memory storage for access tokens
 */
export const apiClient = axios.create({
  baseURL: API_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  withCredentials: true, // Important: send/receive cookies
});

/**
 * Request interceptor - Add JWT token to all requests
 */
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    // Add access token to Authorization header
    const token = getAccessToken();
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

/**
 * Response interceptor - Handle token refresh and errors
 */
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

    // Handle 401 Unauthorized - Try to refresh token
    if (error.response?.status === 401 && originalRequest && !originalRequest._retry) {
      // Skip refresh for auth endpoints
      const isAuthEndpoint = originalRequest.url?.includes('/auth/login') ||
                             originalRequest.url?.includes('/auth/register') ||
                             originalRequest.url?.includes('/auth/refresh');

      if (isAuthEndpoint) {
        return Promise.reject(error);
      }

      // Mark this request as retried to avoid infinite loops
      originalRequest._retry = true;

      if (!isRefreshing) {
        isRefreshing = true;

        try {
          const newToken = await refreshAccessToken();

          if (newToken) {
            isRefreshing = false;
            onTokenRefreshed(newToken);

            // Retry the original request with new token
            if (originalRequest.headers) {
              originalRequest.headers.Authorization = `Bearer ${newToken}`;
            }
            return apiClient(originalRequest);
          }
        } catch (refreshError) {
          isRefreshing = false;
          return Promise.reject(refreshError);
        }
      } else {
        // Wait for the ongoing refresh to complete
        return new Promise((resolve) => {
          subscribeTokenRefresh((token: string) => {
            if (originalRequest.headers) {
              originalRequest.headers.Authorization = `Bearer ${token}`;
            }
            resolve(apiClient(originalRequest));
          });
        });
      }
    }

    // Handle 403 Forbidden
    if (error.response?.status === 403) {
      console.error('Access forbidden:', error.response.data);
    }

    // Retry logic for retryable errors (5xx, network, timeout) on idempotent methods
    if (originalRequest && isRetryableError(error) && isRetryableMethod(originalRequest)) {
      return retryRequest(error, originalRequest);
    }

    return Promise.reject(error);
  }
);

export default apiClient;
