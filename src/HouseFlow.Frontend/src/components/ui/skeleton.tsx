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
