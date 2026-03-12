import { cn } from '@/lib/utils';

interface LoadingSpinnerProps {
  size?: 'sm' | 'md' | 'lg';
  className?: string;
}

export function LoadingSpinner({ size = 'md', className }: LoadingSpinnerProps) {
  const sizeClasses = {
    sm: 'h-4 w-4 border-2',
    md: 'h-8 w-8 border-2',
    lg: 'h-12 w-12 border-3',
  };

  return (
    <div
      className={cn(
        'inline-block animate-spin rounded-full border-solid border-indigo-600 border-r-transparent',
        sizeClasses[size],
        className
      )}
      role="status"
      aria-label="Loading"
    >
      <span className="sr-only">Loading...</span>
    </div>
  );
}

interface LoadingStateProps {
  text?: string;
  className?: string;
}

export function LoadingState({ text, className }: LoadingStateProps) {
  return (
    <div className={cn('flex flex-col items-center justify-center gap-3', className)}>
      <LoadingSpinner size="lg" />
      {text && <p className="text-gray-600 dark:text-gray-400">{text}</p>}
    </div>
  );
}
