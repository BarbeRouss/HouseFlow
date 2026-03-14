"use client";

import { use, useMemo } from 'react';
import { useTranslations, useLocale } from 'next-intl';
import { useHouse } from '@/lib/api/hooks';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import Link from 'next/link';
import { Breadcrumb } from '@/components/ui/breadcrumb';
import { HouseDetailSkeleton } from '@/components/ui/skeleton';
import { ScoreRing } from '@/components/ui/score-ring';
import { Check, Clock, AlertTriangle, Plus, ChevronRight, Home } from 'lucide-react';

// Device type to emoji mapping
const deviceEmojis: Record<string, string> = {
  'Chaudière Gaz': '🔥',
  'Chaudière Fioul': '🔥',
  'Pompe à Chaleur': '❄️',
  'Climatisation': '❄️',
  'Poêle à Bois': '🪵',
  'Toiture': '🏠',
  'Détecteur de Fumée': '🚨',
  'Alarme': '🚨',
  'Chauffe-eau': '🚿',
  'VMC': '💨',
  'default': '🔧',
};

export default function HouseDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const locale = useLocale();
  const t = useTranslations('houses');
  const tDevices = useTranslations('devices');
  const tCommon = useTranslations('common');
  const tMaintenance = useTranslations('maintenance');

  const { data: house, isLoading: houseLoading } = useHouse(id);

  // Sort devices: overdue first, then pending, then up-to-date
  // Must be called before conditional returns to respect React Hooks rules
  const sortedDevices = useMemo(() => {
    if (!house?.devices) return [];
    return [...house.devices].sort((a, b) => {
      // Priority: overdue (highest) > pending > up-to-date (lowest)
      const getPriority = (device: typeof a) => {
        if (device.overdueCount > 0) return 0; // Highest priority
        if (device.pendingCount > 0) return 1;
        return 2; // Up to date - lowest priority
      };
      const priorityDiff = getPriority(a) - getPriority(b);
      if (priorityDiff !== 0) return priorityDiff;
      // Same priority: sort by score ascending (lower score first)
      return a.score - b.score;
    });
  }, [house?.devices]);

  if (houseLoading) {
    return <HouseDetailSkeleton />;
  }

  if (!house) {
    return <div className="p-8">{t('notFound')}</div>;
  }

  // Calculate progress
  const upToDateCount = house.devices?.filter(d => d.score === 100).length || 0;
  const totalDevices = house.devices?.length || 0;

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900 p-4 sm:p-8">
      <div className="max-w-7xl mx-auto">
        {/* Breadcrumb */}
        <Breadcrumb items={[{ label: house.name }]} />

        {/* House Header */}
        <Card className="mb-8 bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm border-white/50">
          <CardContent className="p-6 sm:p-8">
            <div className="flex flex-col lg:flex-row lg:items-center gap-6">
              {/* Left: House info */}
              <div className="flex items-start gap-4 flex-1">
                <div className="w-16 h-16 bg-gradient-to-br from-blue-100 to-blue-200 dark:from-blue-900/30 dark:to-blue-800/30 rounded-2xl flex items-center justify-center shadow-sm flex-shrink-0">
                  <Home className="h-8 w-8 text-blue-600 dark:text-blue-400" />
                </div>
                <div className="flex-1">
                  <div className="flex items-center gap-3 mb-1">
                    <h1 className="text-2xl font-extrabold text-gray-900 dark:text-white">
                      {house.name}
                    </h1>
                  </div>
                  {(house.address || house.city || house.zipCode) && (
                    <p className="text-gray-500 dark:text-gray-400 mb-3">
                      {[house.address, house.city, house.zipCode].filter(Boolean).join(', ')}
                    </p>
                  )}

                  {/* Progress bar */}
                  {totalDevices > 0 && (
                    <div className="max-w-md">
                      <div className="flex justify-between text-sm mb-2">
                        <span className="text-gray-500 dark:text-gray-400">Progression</span>
                        <span className={`font-semibold ${
                          house.score >= 80 ? 'text-green-600' :
                          house.score >= 50 ? 'text-orange-600' : 'text-red-600'
                        }`}>
                          {upToDateCount} sur {totalDevices} à jour
                        </span>
                      </div>
                      <div className="h-3 bg-gray-100 dark:bg-gray-700 rounded-full overflow-hidden">
                        <div
                          className={`h-full rounded-full transition-all duration-500 ${
                            house.score >= 80 ? 'bg-gradient-to-r from-green-400 to-emerald-500' :
                            house.score >= 50 ? 'bg-gradient-to-r from-orange-400 to-orange-500' :
                            'bg-gradient-to-r from-red-400 to-rose-500'
                          }`}
                          style={{ width: `${house.score}%` }}
                        />
                      </div>
                    </div>
                  )}
                </div>
              </div>

              {/* Right: Score circle and CTA */}
              <div className="flex items-center gap-6">
                <div className="hidden sm:block">
                  <ScoreRing score={house.score} size="lg" />
                </div>

                <Link href={`/${locale}/houses/${id}/devices/new`}>
                  <Button className="bg-gradient-to-r from-blue-500 to-blue-600 hover:from-blue-600 hover:to-blue-700 shadow-lg shadow-blue-500/30">
                    <Plus className="h-5 w-5 mr-2" />
                    {tDevices('addDevice')}
                  </Button>
                </Link>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Section title with status badges */}
        <div className="flex flex-wrap items-center justify-between gap-4 mb-6">
          <h2 className="text-xl font-bold text-gray-900 dark:text-white">
            {tDevices('title')}
          </h2>
          {totalDevices > 0 && (
            <div className="flex items-center gap-2">
              {upToDateCount > 0 && (
                <span className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-300 rounded-full text-sm font-medium">
                  <Check className="h-4 w-4" />
                  {upToDateCount} OK
                </span>
              )}
              {house.pendingCount > 0 && (
                <span className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-orange-100 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300 rounded-full text-sm font-medium">
                  {house.pendingCount} {tMaintenance('pending')}
                </span>
              )}
              {house.overdueCount > 0 && (
                <span className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-300 rounded-full text-sm font-medium">
                  {house.overdueCount} {tMaintenance('overdue')}
                </span>
              )}
            </div>
          )}
        </div>

        {/* Devices List */}
        {sortedDevices.length === 0 ? (
          <Card className="bg-white/80 dark:bg-gray-800/80">
            <CardHeader>
              <CardTitle>{tDevices('noDevicesYet')}</CardTitle>
              <CardDescription>
                {tDevices('noDevicesDescription')}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Link href={`/${locale}/houses/${id}/devices/new`}>
                <Button>{tDevices('addDevice')}</Button>
              </Link>
            </CardContent>
          </Card>
        ) : (
          <div className="space-y-4">
            {sortedDevices.map((device) => {
              const deviceEmoji = deviceEmojis[device.type] || deviceEmojis['default'];
              const isOverdue = device.overdueCount > 0;
              const isPending = device.pendingCount > 0 && !isOverdue;
              const isUpToDate = !isOverdue && !isPending;

              return (
                <Link key={device.id} href={`/${locale}/devices/${device.id}`}>
                  <Card
                    className={`group bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm transition-all hover:shadow-lg hover:-translate-y-0.5 cursor-pointer ${
                      isOverdue
                        ? 'border-red-200 dark:border-red-800'
                        : isPending
                        ? 'border-orange-200 dark:border-orange-800'
                        : 'border-green-100 dark:border-green-900'
                    }`}
                  >
                    {/* Overdue banner */}
                    {isOverdue && (
                      <div className="absolute top-0 right-0 bg-red-500 text-white text-xs font-bold px-3 py-1 rounded-bl-xl rounded-tr-lg">
                        EN RETARD
                      </div>
                    )}

                    <CardContent className="p-5">
                      <div className="flex items-center gap-4">
                        {/* Device emoji */}
                        <div className={`w-14 h-14 rounded-xl flex items-center justify-center flex-shrink-0 ${
                          isOverdue
                            ? 'bg-gradient-to-br from-red-100 to-red-200 dark:from-red-900/30 dark:to-red-800/30'
                            : isPending
                            ? 'bg-gradient-to-br from-orange-100 to-orange-200 dark:from-orange-900/30 dark:to-orange-800/30'
                            : 'bg-gradient-to-br from-green-100 to-emerald-200 dark:from-green-900/30 dark:to-green-800/30'
                        }`}>
                          <span className="text-2xl">{deviceEmoji}</span>
                        </div>

                        {/* Device info */}
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2 mb-1">
                            <h3 className="font-bold text-gray-900 dark:text-white">
                              {device.name}
                            </h3>
                            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold ${
                              isOverdue
                                ? 'bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-300'
                                : isPending
                                ? 'bg-orange-100 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300'
                                : 'bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-300'
                            }`}>
                              {isOverdue && <AlertTriangle className="h-3 w-3" />}
                              {isPending && <Clock className="h-3 w-3" />}
                              {isUpToDate && <Check className="h-3 w-3" />}
                              {isOverdue ? tMaintenance('overdue') :
                               isPending ? `${device.pendingCount} ${tMaintenance('pending')}` :
                               tMaintenance('upToDate')}
                            </span>
                          </div>
                          <p className="text-sm text-gray-500 dark:text-gray-400 truncate">
                            {device.type}
                          </p>

                          {/* Mini progress bar */}
                          <div className="mt-2 flex items-center gap-3">
                            <div className="flex-1 max-w-[200px] h-1.5 bg-gray-100 dark:bg-gray-700 rounded-full overflow-hidden">
                              <div
                                className={`h-full rounded-full ${
                                  isOverdue
                                    ? 'bg-gradient-to-r from-red-400 to-red-500'
                                    : isPending
                                    ? 'bg-gradient-to-r from-orange-400 to-orange-500'
                                    : 'bg-gradient-to-r from-green-400 to-emerald-500'
                                }`}
                                style={{ width: `${device.score}%` }}
                              />
                            </div>
                            <span className="text-xs text-gray-500 dark:text-gray-400">
                              {device.score}%
                            </span>
                          </div>
                        </div>

                        {/* Arrow */}
                        <div className="w-10 h-10 rounded-full bg-gray-100 dark:bg-gray-700 flex items-center justify-center group-hover:bg-blue-100 dark:group-hover:bg-blue-900/30 transition">
                          <ChevronRight className="h-5 w-5 text-gray-400 group-hover:text-blue-600 dark:group-hover:text-blue-400 transition" />
                        </div>
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
