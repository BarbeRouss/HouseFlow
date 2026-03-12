"use client";

import { useMemo } from 'react';
import { useTranslations, useLocale } from 'next-intl';
import { useHouses } from '@/lib/api/hooks';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import Link from 'next/link';
import { useAuth } from '@/lib/auth/context';
import { HousesGridSkeleton } from '@/components/ui/skeleton';
import { ScoreRing } from '@/components/ui/score-ring';
import { Check, Clock, AlertTriangle, Plus, Home, ChevronRight } from 'lucide-react';

export default function DashboardPage() {
  const locale = useLocale();
  const t = useTranslations('dashboard');
  const tHouses = useTranslations('houses');
  const tMaintenance = useTranslations('maintenance');
  const tCommon = useTranslations('common');
  const { user } = useAuth();

  const { data: housesData, isLoading } = useHouses();
  const houses = useMemo(() => housesData?.houses || [], [housesData]);

  // Calculate global stats
  const globalScore = housesData?.globalScore || 0;
  const totalUpToDate = houses.reduce((acc, h) => acc + (h.score === 100 ? 1 : 0), 0);
  const totalPending = houses.reduce((acc, h) => acc + h.pendingCount, 0);
  const totalOverdue = houses.reduce((acc, h) => acc + h.overdueCount, 0);

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900 p-4 sm:p-8">
      <div className="max-w-7xl mx-auto">
        {/* Hero section with global score */}
        <Card className="mb-8 bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm border-white/50">
          <CardContent className="p-6 sm:p-8">
            <div className="flex flex-col lg:flex-row lg:items-center gap-8">
              {/* Left: Score circle - responsive */}
              <div className="flex-shrink-0 flex justify-center lg:justify-start">
                <div className="hidden sm:block">
                  <ScoreRing score={globalScore} size="lg" />
                </div>
                <div className="sm:hidden">
                  <ScoreRing score={globalScore} size="md" />
                </div>
              </div>

              {/* Right: Welcome + message */}
              <div className="flex-1 text-center lg:text-left">
                <p className={`text-sm font-semibold mb-1 flex items-center justify-center lg:justify-start gap-2 ${
                  globalScore >= 80 ? 'text-green-600' :
                  globalScore >= 50 ? 'text-orange-600' : 'text-red-600'
                }`}>
                  <span className={`w-2 h-2 rounded-full animate-pulse ${
                    globalScore >= 80 ? 'bg-green-500' :
                    globalScore >= 50 ? 'bg-orange-500' : 'bg-red-500'
                  }`} />
                  {globalScore >= 80 ? 'Bonne forme !' :
                   globalScore >= 50 ? 'Quelques actions requises' : 'Attention requise'}
                </p>
                <h1 className="text-2xl sm:text-3xl font-extrabold text-gray-900 dark:text-white mb-2">
                  {t('welcome')} {user?.firstName} !
                </h1>
                <p className="text-gray-500 dark:text-gray-400 mb-4">
                  {houses.length > 0
                    ? `${houses.length} maison${houses.length > 1 ? 's' : ''} sous votre supervision`
                    : t('getStarted')}
                </p>

                {/* Quick action badges */}
                {houses.length > 0 && (
                  <div className="flex flex-wrap gap-2 justify-center lg:justify-start">
                    {totalUpToDate > 0 && (
                      <span className="inline-flex items-center gap-2 px-4 py-2 bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-300 rounded-full text-sm font-semibold">
                        <Check className="h-4 w-4" />
                        {totalUpToDate} à jour
                      </span>
                    )}
                    {totalPending > 0 && (
                      <span className="inline-flex items-center gap-2 px-4 py-2 bg-orange-100 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300 rounded-full text-sm font-semibold">
                        <Clock className="h-4 w-4" />
                        {totalPending} à faire
                      </span>
                    )}
                    {totalOverdue > 0 && (
                      <span className="inline-flex items-center gap-2 px-4 py-2 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-300 rounded-full text-sm font-semibold">
                        <AlertTriangle className="h-4 w-4" />
                        {totalOverdue} en retard
                      </span>
                    )}
                  </div>
                )}
              </div>

              {/* CTA Button */}
              <div className="flex-shrink-0 flex justify-center lg:justify-end">
                <Link href={`/${locale}/houses/new`}>
                  <Button className="bg-gradient-to-r from-blue-500 to-blue-600 hover:from-blue-600 hover:to-blue-700 shadow-lg shadow-blue-500/30">
                    <Plus className="h-5 w-5 mr-2" />
                    {tHouses('addHouse')}
                  </Button>
                </Link>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Section title */}
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-xl font-bold text-gray-900 dark:text-white">{t('myHouses')}</h2>
          <span className="text-sm text-gray-500 dark:text-gray-400">
            {houses.length} propriété{houses.length !== 1 ? 's' : ''}
          </span>
        </div>

        {/* Houses grid */}
        {isLoading ? (
          <HousesGridSkeleton />
        ) : houses.length === 0 ? (
          <Card className="bg-white/80 dark:bg-gray-800/80">
            <CardHeader>
              <CardTitle>{t('noHousesYet')}</CardTitle>
              <CardDescription>
                {t('getStarted')}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Link href={`/${locale}/houses/new`}>
                <Button>{tHouses('addHouse')}</Button>
              </Link>
            </CardContent>
          </Card>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            {houses.map((house) => {
              const isOverdue = house.overdueCount > 0;
              const isPending = house.pendingCount > 0 && !isOverdue;

              return (
                <Link key={house.id} href={`/${locale}/houses/${house.id}`}>
                  <Card className={`group h-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm transition-all hover:shadow-lg hover:-translate-y-1 cursor-pointer overflow-hidden ${
                    isOverdue
                      ? 'border-red-200 dark:border-red-800'
                      : isPending
                      ? 'border-orange-200 dark:border-orange-800'
                      : house.score === 100
                      ? 'border-green-200 dark:border-green-800'
                      : 'border-white/50'
                  }`}>
                    {/* Color bar at top */}
                    <div className={`h-2 ${
                      isOverdue
                        ? 'bg-gradient-to-r from-red-400 to-rose-500'
                        : isPending
                        ? 'bg-gradient-to-r from-orange-400 to-orange-500'
                        : 'bg-gradient-to-r from-green-400 to-emerald-500'
                    }`} />

                    {/* Perfect badge */}
                    {house.score === 100 && (
                      <div className="absolute top-4 right-4 z-10">
                        <span className="inline-flex items-center gap-1 px-2 py-1 bg-green-500 text-white rounded-full text-xs font-bold shadow-lg shadow-green-500/30">
                          <Check className="h-3 w-3" />
                          Parfait
                        </span>
                      </div>
                    )}

                    <CardContent className="p-6">
                      <div className="flex items-start justify-between mb-4">
                        <div className="w-14 h-14 bg-gradient-to-br from-blue-100 to-blue-200 dark:from-blue-900/30 dark:to-blue-800/30 rounded-2xl flex items-center justify-center shadow-sm">
                          <Home className="h-7 w-7 text-blue-600 dark:text-blue-400" />
                        </div>

                        {/* Mini progress circle */}
                        <ScoreRing score={house.score} size="sm" />
                      </div>

                      <h3 className="text-lg font-bold text-gray-900 dark:text-white mb-1">
                        {house.name}
                      </h3>
                      <p className="text-gray-500 dark:text-gray-400 text-sm mb-4">
                        {house.address && house.city
                          ? `${house.address}, ${house.city}`
                          : `${house.devicesCount} appareil${house.devicesCount !== 1 ? 's' : ''}`}
                      </p>

                      {/* Progress bar */}
                      <div className="mb-4">
                        <div className="flex justify-between text-xs mb-1">
                          <span className="text-gray-500 dark:text-gray-400">
                            {house.devicesCount} appareil{house.devicesCount !== 1 ? 's' : ''}
                          </span>
                          <span className={`font-semibold ${
                            isOverdue ? 'text-red-600' :
                            isPending ? 'text-orange-600' : 'text-green-600'
                          }`}>
                            {isOverdue ? `${house.overdueCount} en retard` :
                             isPending ? `${house.pendingCount} restant${house.pendingCount > 1 ? 's' : ''}` :
                             'Complet !'}
                          </span>
                        </div>
                        <div className="h-2 bg-gray-100 dark:bg-gray-700 rounded-full overflow-hidden">
                          <div
                            className={`h-full rounded-full transition-all ${
                              isOverdue
                                ? 'bg-gradient-to-r from-red-400 to-rose-500'
                                : isPending
                                ? 'bg-gradient-to-r from-orange-400 to-orange-500'
                                : 'bg-gradient-to-r from-green-400 to-emerald-500'
                            }`}
                            style={{ width: `${house.score}%` }}
                          />
                        </div>
                      </div>

                      <div className="flex items-center justify-between">
                        <span className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-sm font-medium ${
                          isOverdue
                            ? 'bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-300'
                            : isPending
                            ? 'bg-orange-100 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300'
                            : 'bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-300'
                        }`}>
                          {isOverdue && <AlertTriangle className="h-4 w-4" />}
                          {isPending && <Clock className="h-4 w-4" />}
                          {!isOverdue && !isPending && <Check className="h-4 w-4" />}
                          {house.devicesCount} appareil{house.devicesCount !== 1 ? 's' : ''}
                        </span>
                        <span className="text-sm text-gray-400 group-hover:text-blue-500 transition flex items-center gap-1">
                          Voir
                          <ChevronRight className="h-4 w-4" />
                        </span>
                      </div>
                    </CardContent>
                  </Card>
                </Link>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
