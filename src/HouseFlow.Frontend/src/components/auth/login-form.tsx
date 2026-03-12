"use client";

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations, useLocale } from 'next-intl';
import { useLogin } from '@/lib/api/hooks';

export function LoginForm() {
  const router = useRouter();
  const locale = useLocale();
  const t = useTranslations('auth');
  const tCommon = useTranslations('common');

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');

  const loginMutation = useLogin({
    onSuccess: () => {
      router.push(`/${locale}/dashboard`);
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

      <div className="text-center text-sm text-gray-600 dark:text-gray-400">
        {t('dontHaveAccount')}{' '}
        <Link href={`/${locale}/register`} className="text-blue-600 dark:text-blue-400 hover:underline">
          {t('register')}
        </Link>
      </div>
    </form>
  );
}
