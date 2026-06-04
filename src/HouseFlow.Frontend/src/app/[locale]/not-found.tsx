import Link from "next/link";
import { useTranslations } from "next-intl";

export default function LocaleNotFound() {
  const t = useTranslations("errors");

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-gray-900 p-8">
      <div className="text-center max-w-md">
        <p className="text-7xl font-bold text-gray-200 dark:text-gray-700 mb-2">
          404
        </p>
        <h1 className="text-xl font-semibold text-gray-900 dark:text-white mb-2">
          {t("notFoundTitle")}
        </h1>
        <p className="text-sm text-gray-500 dark:text-gray-400 mb-8">
          {t("notFoundDescription")}
        </p>
        <Link
          href="/"
          className="inline-block rounded-lg bg-gradient-to-r from-blue-500 to-blue-600 px-6 py-2.5 text-sm font-medium text-white hover:from-blue-600 hover:to-blue-700 transition-all"
        >
          {t("backToHome")}
        </Link>
      </div>
    </div>
  );
}
