import { cn } from '@/lib/utils';

interface SkeletonProps {
  className?: string;
}

export function Skeleton({ className }: SkeletonProps) {
  return (
    <div
      className={cn(
        'animate-pulse rounded-md bg-gray-200 dark:bg-gray-700',
        className
      )}
    />
  );
}

// Skeleton for house/device cards
export function CardSkeleton() {
  return (
    <div className="border border-gray-200 dark:border-gray-700 rounded-lg p-6 space-y-4">
      <Skeleton className="h-6 w-3/4" />
      <Skeleton className="h-4 w-full" />
      <Skeleton className="h-10 w-full" />
    </div>
  );
}

// Skeleton for device detail page
export function DeviceDetailSkeleton() {
  return (
    <div className="min-h-screen bg-gray-50 dark:bg-gray-900 p-8">
      <div className="max-w-7xl mx-auto space-y-8">
        {/* Header skeleton */}
        <div>
          <Skeleton className="h-10 w-64 mb-2" />
          <Skeleton className="h-6 w-48" />
        </div>

        {/* Maintenance types card skeleton */}
        <div className="border border-gray-200 dark:border-gray-700 rounded-lg p-6">
          <div className="flex items-center justify-between mb-4">
            <div>
              <Skeleton className="h-7 w-48 mb-2" />
              <Skeleton className="h-5 w-32" />
            </div>
            <Skeleton className="h-10 w-48" />
          </div>
          <div className="space-y-3">
            <Skeleton className="h-20 w-full" />
            <Skeleton className="h-20 w-full" />
          </div>
        </div>

        {/* Maintenance history card skeleton */}
        <div className="border border-gray-200 dark:border-gray-700 rounded-lg p-6">
          <Skeleton className="h-7 w-48 mb-2" />
          <Skeleton className="h-5 w-32 mb-4" />
          <div className="space-y-3">
            <Skeleton className="h-24 w-full" />
            <Skeleton className="h-24 w-full" />
          </div>
        </div>
      </div>
    </div>
  );
}

// Skeleton for dashboard houses grid
export function HousesGridSkeleton() {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
      <CardSkeleton />
      <CardSkeleton />
      <CardSkeleton />
    </div>
  );
}

// Skeleton for house detail page
export function HouseDetailSkeleton() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900 p-4 sm:p-8">
      <div className="max-w-7xl mx-auto">
        {/* Breadcrumb skeleton */}
        <div className="flex items-center gap-2 mb-6">
          <Skeleton className="h-4 w-4" />
          <Skeleton className="h-4 w-16" />
          <Skeleton className="h-4 w-4" />
          <Skeleton className="h-4 w-32" />
        </div>

        {/* Header card skeleton */}
        <div className="border border-gray-200 dark:border-gray-700 rounded-lg p-6 sm:p-8 mb-8 bg-white/80 dark:bg-gray-800/80">
          <div className="flex flex-col lg:flex-row lg:items-center gap-6">
            <div className="flex items-start gap-4 flex-1">
              <Skeleton className="w-16 h-16 rounded-2xl" />
              <div className="flex-1">
                <Skeleton className="h-8 w-48 mb-2" />
                <Skeleton className="h-4 w-64 mb-4" />
                <Skeleton className="h-3 w-full max-w-md" />
              </div>
            </div>
            <Skeleton className="h-20 w-20 rounded-full hidden sm:block" />
          </div>
        </div>

        {/* Devices list skeleton */}
        <div className="flex items-center justify-between mb-6">
          <Skeleton className="h-7 w-32" />
          <Skeleton className="h-6 w-20" />
        </div>
        <div className="space-y-4">
          {[1, 2, 3].map((i) => (
            <div key={i} className="border border-gray-200 dark:border-gray-700 rounded-lg p-5 bg-white/80 dark:bg-gray-800/80">
              <div className="flex items-center gap-4">
                <Skeleton className="w-14 h-14 rounded-xl" />
                <div className="flex-1">
                  <Skeleton className="h-5 w-40 mb-2" />
                  <Skeleton className="h-4 w-24 mb-2" />
                  <Skeleton className="h-1.5 w-48" />
                </div>
                <Skeleton className="w-10 h-10 rounded-full" />
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// Skeleton for dashboard page (hero + upcoming tasks + houses grid)
export function DashboardSkeleton() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900 p-4 sm:p-8">
      <div className="max-w-7xl mx-auto">
        {/* Hero section skeleton */}
        <div className="mb-8 border border-gray-200 dark:border-gray-700 rounded-lg bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm">
          <div className="p-6 sm:p-8">
            <div className="flex flex-col lg:flex-row lg:items-center gap-8">
              {/* Score ring placeholder */}
              <div className="flex-shrink-0 flex justify-center lg:justify-start">
                <Skeleton className="hidden sm:block w-40 h-40 rounded-full" />
                <Skeleton className="sm:hidden w-28 h-28 rounded-full" />
              </div>

              {/* Welcome text placeholder */}
              <div className="flex-1 flex flex-col items-center lg:items-start gap-2">
                <Skeleton className="h-4 w-32" />
                <Skeleton className="h-8 w-64" />
                <Skeleton className="h-4 w-48" />
                <div className="flex gap-2 mt-2">
                  <Skeleton className="h-9 w-28 rounded-full" />
                  <Skeleton className="h-9 w-28 rounded-full" />
                </div>
              </div>

              {/* CTA button placeholder */}
              <div className="flex-shrink-0 flex justify-center lg:justify-end">
                <Skeleton className="h-10 w-40 rounded-md" />
              </div>
            </div>
          </div>
        </div>

        {/* Upcoming tasks skeleton */}
        <div className="mb-8">
          <div className="flex items-center justify-between mb-4">
            <Skeleton className="h-7 w-48" />
            <div className="flex gap-2">
              <Skeleton className="h-7 w-12 rounded-full" />
              <Skeleton className="h-7 w-12 rounded-full" />
            </div>
          </div>
          <div className="space-y-3">
            {[1, 2, 3].map((i) => (
              <div key={i} className="border border-gray-200 dark:border-gray-700 rounded-lg p-4 bg-white/80 dark:bg-gray-800/80 border-l-4 border-l-gray-300 dark:border-l-gray-600">
                <div className="flex items-center justify-between gap-4">
                  <div className="flex items-center gap-3 flex-1">
                    <Skeleton className="w-10 h-10 rounded-lg flex-shrink-0" />
                    <div className="flex-1">
                      <Skeleton className="h-5 w-40 mb-1" />
                      <Skeleton className="h-4 w-56" />
                    </div>
                  </div>
                  <Skeleton className="h-4 w-24 hidden sm:block" />
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Houses grid section title */}
        <div className="flex items-center justify-between mb-6">
          <Skeleton className="h-7 w-32" />
          <Skeleton className="h-5 w-20" />
        </div>

        {/* Houses grid skeleton */}
        <HousesGridSkeleton />
      </div>
    </div>
  );
}

// Skeleton for list items (maintenance, devices)
export function ListItemSkeleton() {
  return (
    <div className="p-4 bg-gray-50 dark:bg-gray-800 rounded-md space-y-2">
      <div className="flex items-start justify-between">
        <Skeleton className="h-5 w-32" />
        <Skeleton className="h-6 w-20" />
      </div>
      <Skeleton className="h-4 w-48" />
      <Skeleton className="h-4 w-24" />
    </div>
  );
}
