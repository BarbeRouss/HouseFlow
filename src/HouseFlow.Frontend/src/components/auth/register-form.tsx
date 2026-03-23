"use client";

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations, useLocale } from 'next-intl';
import { useRegister } from '@/lib/api/hooks';

export function RegisterForm() {
  const router = useRouter();
  const locale = useLocale();
  const searchParams = useSearchParams();
  const invitationToken = searchParams.get('invitation');
  const t = useTranslations('auth');
  const tCommon = useTranslations('common');

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');

  const registerMutation = useRegister({
    onSuccess: (data) => {
      if (invitationToken) {
        // If registering via invitation, redirect to invitation acceptance
        router.replace(`/${locale}/invitations/${invitationToken}`);
      } else if (data.firstHouseId) {
        // Redirect to device creation for the first house
        router.replace(`/${locale}/houses/${data.firstHouseId}/devices/new`);
      } else {
        router.replace(`/${locale}/dashboard`);
      }
    },
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    registerMutation.mutate({
      firstName,
      lastName,
      email,
      password,
      ...(invitationToken ? { invitationToken } : {}),
    });
  };

  const getErrorMessage = () => {
    if (!registerMutation.error) return t('registerError');

    // Extract error message from API response
    const error = registerMutation.error as any;
    const apiError = error.response?.data?.error;

    if (apiError) {
      // Map common error messages to translation keys
      if (apiError.includes('already registered') || apiError.includes('already exists')) {
        return t('emailAlreadyExists');
      }
      // For other errors, return the API message (already in English)
      return apiError;
    }

    return t('registerError');
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      {registerMutation.isError && (
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 px-4 py-3 rounded">
          {getErrorMessage()}
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div>
          <label htmlFor="firstName" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            {t('firstName')}
          </label>
          <input
            id="firstName"
            type="text"
            value={firstName}
            onChange={(e) => setFirstName(e.target.value)}
            required
            className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
            placeholder="Jean"
          />
        </div>

        <div>
          <label htmlFor="lastName" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            {t('lastName')}
          </label>
          <input
            id="lastName"
            type="text"
            value={lastName}
            onChange={(e) => setLastName(e.target.value)}
            required
            className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
            placeholder="Dupont"
          />
        </div>
      </div>

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
          minLength={8}
          pattern="^(?=.*\d).{8,}$"
          title={t('passwordRequirement')}
          className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
          placeholder="••••••••••••"
        />
      </div>

      <button
        type="submit"
        disabled={registerMutation.isPending}
        className="w-full bg-blue-600 hover:bg-blue-700 disabled:bg-blue-400 text-white font-semibold py-2 px-4 rounded-md transition-colors"
      >
        {registerMutation.isPending ? tCommon('loading') : t('register')}
      </button>

      <div className="text-center text-sm text-gray-600 dark:text-gray-400">
        {t('alreadyHaveAccount')}{' '}
        <Link href={`/${locale}/login`} className="text-blue-600 dark:text-blue-400 hover:underline">
          {t('login')}
        </Link>
      </div>
    </form>
  );
}
