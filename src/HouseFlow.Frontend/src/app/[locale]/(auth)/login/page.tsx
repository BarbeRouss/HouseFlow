import { LoginForm } from '@/components/auth/login-form';
import { Logo } from '@/components/logo';
import { useTranslations } from 'next-intl';

export default function LoginPage() {
  const t = useTranslations('auth');

  return (
    <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-gray-50 to-gray-100 dark:from-gray-900 dark:to-gray-800 px-4">
      <div className="w-full max-w-md">
        <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl p-4 sm:p-8">
          <div className="text-center mb-8 flex flex-col items-center gap-3">
            <Logo size={56} />
            <h1 className="text-3xl font-bold text-gray-900 dark:text-white">
              HouseFlow
            </h1>
            <p className="text-gray-600 dark:text-gray-400">
              {t('tagline')}
            </p>
          </div>
          <LoginForm />
        </div>
      </div>
    </div>
  );
}
