"use client";

import { useAuth } from '@/lib/auth/context';
import { useRouter } from 'next/navigation';
import { useLocale } from 'next-intl';
import { useEffect } from 'react';

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const { isAuthenticated, isLoading } = useAuth();
  const router = useRouter();
  const locale = useLocale();

  useEffect(() => {
    // Only redirect if auth check is complete and user is not authenticated
    if (!isLoading && !isAuthenticated) {
      router.push(`/${locale}/login`);
    }
  }, [isAuthenticated, isLoading, router, locale]);

  // Don't block rendering - let pages show their own loading states
  // This eliminates the full-screen "Loading..." spinner
  return <>{children}</>;
}
