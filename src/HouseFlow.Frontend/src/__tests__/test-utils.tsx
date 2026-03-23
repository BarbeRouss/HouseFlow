import React from 'react';
import { render, RenderOptions } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

// Mock next-intl
vi.mock('next-intl', () => ({
  useTranslations: (namespace: string) => {
    return (key: string, params?: Record<string, unknown>) => {
      if (params) {
        let result = `${namespace}.${key}`;
        for (const [k, v] of Object.entries(params)) {
          result = result.replace(`{${k}}`, String(v));
        }
        return result;
      }
      return `${namespace}.${key}`;
    };
  },
  useLocale: () => 'fr',
}));

// Mock next/navigation
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
    back: vi.fn(),
    prefetch: vi.fn(),
  }),
  usePathname: () => '/fr/dashboard',
  useSearchParams: () => new URLSearchParams(),
}));

/**
 * Create a fresh QueryClient for testing
 */
export function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: 0,
      },
      mutations: {
        retry: false,
      },
    },
  });
}

/**
 * Wrapper that provides QueryClient + any other providers needed for testing
 */
export function createWrapper() {
  const queryClient = createTestQueryClient();
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        {children}
      </QueryClientProvider>
    );
  };
}

/**
 * Custom render that wraps components with all necessary providers
 */
export function renderWithProviders(
  ui: React.ReactElement,
  options?: Omit<RenderOptions, 'wrapper'>
) {
  const queryClient = createTestQueryClient();
  function Wrapper({ children }: { children: React.ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        {children}
      </QueryClientProvider>
    );
  }
  return { ...render(ui, { wrapper: Wrapper, ...options }), queryClient };
}
