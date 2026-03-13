"use client";

import Link from 'next/link';
import { useLocale, useTranslations } from 'next-intl';
import { ChevronRight, Home } from 'lucide-react';

export interface BreadcrumbItem {
  label: string;
  href?: string;
}

interface BreadcrumbProps {
  items: BreadcrumbItem[];
}

export function Breadcrumb({ items }: BreadcrumbProps) {
  const locale = useLocale();
  const t = useTranslations('header');

  return (
    <nav className="flex items-center gap-2 text-sm mb-6 flex-wrap">
      <Link
        href={`/${locale}/dashboard`}
        className="text-gray-500 hover:text-blue-600 dark:hover:text-blue-400 transition flex items-center gap-1"
      >
        <Home className="h-4 w-4" />
        <span className="hidden sm:inline">{t('home')}</span>
      </Link>

      {items.map((item, index) => (
        <div key={index} className="flex items-center gap-2">
          <ChevronRight className="h-4 w-4 text-gray-300" />
          {item.href ? (
            <Link
              href={item.href}
              className="text-gray-500 hover:text-blue-600 transition"
            >
              {item.label}
            </Link>
          ) : (
            <span className="text-gray-900 dark:text-white font-medium">
              {item.label}
            </span>
          )}
        </div>
      ))}
    </nav>
  );
}
