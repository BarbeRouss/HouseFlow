import { RegisterForm } from '@/components/auth/register-form';
import { useTranslations } from 'next-intl';

export default function RegisterPage() {
  const t = useTranslations('auth');

  return (
    <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-gray-50 to-gray-100 dark:from-gray-900 dark:to-gray-800 px-4">
      <div className="w-full max-w-md">
        <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl p-4 sm:p-8">
          <div className="text-center mb-8">
            <h1 className="text-3xl font-bold text-gray-900 dark:text-white">
              HouseFlow
            </h1>
            <p className="text-gray-600 dark:text-gray-400 mt-2">
              {t('createAccount')}
            </p>
          </div>
          <RegisterForm />
        </div>
      </div>
    </div>
  );
}
