"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { useState } from "react";

export function QueryProvider({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            // Optimized staleTime for better performance
            // Data stays fresh for 5 minutes before being considered stale
            staleTime: 5 * 60 * 1000, // 5 minutes

            // Cache data for 10 minutes (garbage collection time)
            gcTime: 10 * 60 * 1000, // 10 minutes

            // Retry failed requests only once
            retry: 1,

            // Don't refetch on window focus - reduces unnecessary API calls
            refetchOnWindowFocus: false,

            // Refetch on mount only if data is stale
            // This allows fresh data to be reused while invalidated data refetches
            refetchOnMount: true,
          },
        },
      })
  );

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  );
}
