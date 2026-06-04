"use client";

import { useTranslations } from 'next-intl';
import { cn } from '@/lib/utils';
import { useRetryState } from '@/lib/api/hooks/use-retry-state';

export function RetryIndicator() {
  const isRetrying = useRetryState();
  const t = useTranslations('common');

  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        'fixed bottom-4 left-1/2 -translate-x-1/2 z-50',
        'flex items-center gap-2 rounded-full bg-amber-100 dark:bg-amber-900 px-4 py-2 shadow-lg',
        'text-sm text-amber-800 dark:text-amber-200',
        'transition-all duration-300',
        isRetrying
          ? 'translate-y-0 opacity-100'
          : 'translate-y-4 opacity-0 pointer-events-none',
      )}
    >
      <div className="h-3 w-3 animate-spin rounded-full border-2 border-solid border-amber-600 border-r-transparent" />
      <span>{t('retrying')}</span>
    </div>
  );
}
