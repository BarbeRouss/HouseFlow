"use client";

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useLocale } from 'next-intl';
import { useAuth } from '@/lib/auth/context';
import { consumeFormRedirecting } from '@/lib/auth/redirect-guard';
import { ErrorBoundary } from '@/components/ui/error-boundary';

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();
  const router = useRouter();
  const locale = useLocale();

  useEffect(() => {
    if (!isLoading && isAuthenticated) {
      // If a form just authenticated and is handling its own redirect
      // (e.g. register → /houses/{id}/devices/new), don't interfere.
      if (consumeFormRedirecting()) {
        return;
      }
      // Otherwise, the user navigated to an auth page while already logged in
      // (e.g. typing /login in URL bar). Redirect to dashboard.
      router.replace(`/${locale}/dashboard`);
    }
  }, [isAuthenticated, isLoading, router, locale]);

  if (isLoading || isAuthenticated) {
    return null;
  }

  return (
    <ErrorBoundary>
      {children}
    </ErrorBoundary>
  );
}
