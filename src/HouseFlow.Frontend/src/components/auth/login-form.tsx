"use client";

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations, useLocale } from 'next-intl';
import { useTheme } from 'next-themes';
import { useLogin } from '@/lib/api/hooks';
import { locales } from '@/lib/i18n/config';

export function LoginForm() {
  const router = useRouter();
  const locale = useLocale();
  const t = useTranslations('auth');
  const tCommon = useTranslations('common');
  const { setTheme } = useTheme();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');

  const isDemoMode = typeof window !== 'undefined' && (window as any).__RUNTIME_CONFIG__?.DEMO_MODE === true;

  const loginMutation = useLogin({
    onSuccess: (data) => {
      // Apply user's saved theme preference
      const userTheme = data.user.theme;
      if (userTheme && ['light', 'dark', 'system'].includes(userTheme)) {
        setTheme(userTheme);
      }

      // Redirect to user's preferred language
      const userLang = data.user.language;
      const targetLocale = userLang && locales.includes(userLang as any) ? userLang : locale;
      router.replace(`/${targetLocale}/dashboard`);
    },
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    loginMutation.mutate({ email, password });
  };

  const getErrorMessage = () => {
    if (!loginMutation.error) return t('loginError');

    // Extract error message from API response
    const error = loginMutation.error as any;
    if (error.response?.data?.error) {
      return error.response.data.error;
    }

    return t('loginError');
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      {loginMutation.isError && (
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 px-4 py-3 rounded">
          {getErrorMessage()}
        </div>
      )}

      <div>
        <label htmlFor="email" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
          {t('email')}
        </label>
        <input
          id="email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
          placeholder="you@example.com"
        />
      </div>

      <div>
        <label htmlFor="password" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
          {t('password')}
        </label>
        <input
          id="password"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
          placeholder="••••••••"
        />
      </div>

      <button
        type="submit"
        disabled={loginMutation.isPending}
        className="w-full bg-blue-600 hover:bg-blue-700 disabled:bg-blue-400 text-white font-semibold py-2 px-4 rounded-md transition-colors"
      >
        {loginMutation.isPending ? tCommon('loading') : t('login')}
      </button>

      {isDemoMode && (
        <div className="relative">
          <div className="absolute inset-0 flex items-center">
            <div className="w-full border-t border-gray-300 dark:border-gray-600" />
          </div>
          <div className="relative flex justify-center text-xs">
            <span className="bg-white dark:bg-gray-800 px-2 text-gray-500 dark:text-gray-400">
              {t('demoEnvironment')}
            </span>
          </div>
        </div>
      )}

      {isDemoMode && (
        <button
          type="button"
          onClick={() => {
            setEmail('demo@demo.com');
            setPassword('Demo@2026!');
            loginMutation.mutate({ email: 'demo@demo.com', password: 'Demo@2026!' });
          }}
          disabled={loginMutation.isPending}
          className="w-full bg-amber-500 hover:bg-amber-600 disabled:bg-amber-300 text-white font-semibold py-2 px-4 rounded-md transition-colors"
        >
          {t('demoLogin')}
        </button>
      )}

      <div className="text-center text-sm text-gray-600 dark:text-gray-400">
        {t('dontHaveAccount')}{' '}
        <Link href={`/${locale}/register`} className="text-blue-600 dark:text-blue-400 hover:underline">
          {t('register')}
        </Link>
      </div>
    </form>
  );
}
