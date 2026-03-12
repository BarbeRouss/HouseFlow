"use client";

import { useTranslations, useLocale } from 'next-intl';
import { useHouses } from '@/lib/api/hooks';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import Link from 'next/link';

export default function HousesPage() {
  const t = useTranslations('houses');
  const tCommon = useTranslations('common');
  const tMaintenance = useTranslations('maintenance');
  const locale = useLocale();
  const { data: housesData, isLoading } = useHouses();
  const houses = housesData?.houses || [];

  if (isLoading) {
    return <div className="p-8">{tCommon('loading')}</div>;
  }

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-gray-900 p-8">
      <div className="max-w-7xl mx-auto">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-3xl font-bold text-gray-900 dark:text-white">
              {t('title')}
            </h1>
            {housesData?.globalScore !== undefined && (
              <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
                Score global: {housesData.globalScore}%
              </p>
            )}
          </div>
          <Link href={`/${locale}/houses/new`}>
            <Button>{t('addHouse')}</Button>
          </Link>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {houses.map((house) => (
            <Link key={house.id} href={`/${locale}/houses/${house.id}`}>
              <Card className="hover:shadow-lg transition-shadow cursor-pointer">
                <CardHeader>
                  <div className="flex justify-between items-start">
                    <CardTitle>{house.name}</CardTitle>
                    <span className={`px-2 py-1 text-xs rounded-full ${
                      house.score >= 80 ? 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200' :
                      house.score >= 50 ? 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200' :
                      'bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200'
                    }`}>
                      {house.score}%
                    </span>
                  </div>
                  <CardDescription>
                    {house.address && house.city && house.zipCode ? (
                      <>{house.address}, {house.city} {house.zipCode}</>
                    ) : (
                      <>{house.devicesCount} appareil(s)</>
                    )}
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  {(house.pendingCount > 0 || house.overdueCount > 0) && (
                    <div className="mb-3 text-sm">
                      {house.overdueCount > 0 && (
                        <span className="text-red-600 dark:text-red-400 mr-3">
                          {house.overdueCount} {tMaintenance('overdue')}
                        </span>
                      )}
                      {house.pendingCount > 0 && (
                        <span className="text-yellow-600 dark:text-yellow-400">
                          {house.pendingCount} {tMaintenance('pending')}
                        </span>
                      )}
                    </div>
                  )}
                  <Button variant="outline" className="w-full">
                    {tCommon('viewDetails')}
                  </Button>
                </CardContent>
              </Card>
            </Link>
          ))}
        </div>
      </div>
    </div>
  );
}
