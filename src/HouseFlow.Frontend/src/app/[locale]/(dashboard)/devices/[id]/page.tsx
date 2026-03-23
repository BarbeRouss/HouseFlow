"use client";

import { use, useState } from 'react';
import { useTranslations, useLocale } from 'next-intl';
import { useDevice, useMaintenanceHistory } from '@/lib/api/hooks';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { format } from 'date-fns';
import { LogMaintenanceDialog } from '@/components/maintenance/log-maintenance-dialog';
import { AddMaintenanceTypeDialog } from '@/components/maintenance/add-maintenance-type-dialog';
import { DeviceDetailSkeleton, ListItemSkeleton } from '@/components/ui/skeleton';
import { Breadcrumb } from '@/components/ui/breadcrumb';
import { ScoreRing } from '@/components/ui/score-ring';
import { EditDeviceDialog } from '@/components/devices/edit-device-dialog';
import { DeleteDeviceDialog } from '@/components/devices/delete-device-dialog';
import { Check, Clock, AlertTriangle, Plus, Pencil, Trash2, Wrench } from 'lucide-react';
import { EmptyState } from '@/components/ui/empty-state';

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

export default function DeviceDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const locale = useLocale();
  const t = useTranslations('devices');
  const tMaintenance = useTranslations('maintenance');
  const tCommon = useTranslations('common');

  const { data: device, isLoading: deviceLoading } = useDevice(id);
  // maintenanceTypes are included in device response - no separate call needed
  const { data: maintenanceHistory, isLoading: historyLoading } = useMaintenanceHistory(id);

  const [showLogDialog, setShowLogDialog] = useState(false);
  const [showAddTypeDialog, setShowAddTypeDialog] = useState(false);
  const [selectedMaintenanceType, setSelectedMaintenanceType] = useState<string | null>(null);
  const [showEditDialog, setShowEditDialog] = useState(false);
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);

  if (deviceLoading) {
    return <DeviceDetailSkeleton />;
  }

  if (!device) {
    return <div className="p-8">{tCommon('notFound')}</div>;
  }

  const handleLogMaintenance = (maintenanceTypeId: string) => {
    setSelectedMaintenanceType(maintenanceTypeId);
    setShowLogDialog(true);
  };

  const deviceEmoji = deviceEmojis[device.type] || deviceEmojis['default'];

  // Use data from device response (already includes maintenanceTypes)
  const maintenanceTypes = device.maintenanceTypes || [];
  const deviceScore = device.score;
  const totalTypes = maintenanceTypes.length;

  // Get status counts from device's maintenance types
  const pendingCount = maintenanceTypes.filter(t => t.status === 'pending').length;
  const overdueCount = maintenanceTypes.filter(t => t.status === 'overdue').length;

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900 p-4 sm:p-8">
      <div className="max-w-7xl mx-auto">
        {/* Breadcrumb */}
        <Breadcrumb
          items={[
            { label: device.houseName || t('house'), href: `/${locale}/houses/${device.houseId}` },
            { label: device.name },
          ]}
        />

        {/* Device Header */}
        <Card className="mb-8 bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm border-white/50">
          <CardContent className="p-6">
            <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
              {/* Device info */}
              <div className="flex items-start gap-4">
                <div className="w-16 h-16 bg-gradient-to-br from-orange-100 to-orange-200 dark:from-orange-900/30 dark:to-orange-800/30 rounded-2xl flex items-center justify-center flex-shrink-0 shadow-lg">
                  <span className="text-3xl">{deviceEmoji}</span>
                </div>
                <div>
                  <div className="flex items-center gap-3 flex-wrap">
                    <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-white">{device.name}</h1>
                    {overdueCount > 0 && (
                      <span className="inline-flex items-center gap-1 px-3 py-1 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-300 rounded-full text-xs font-semibold">
                        <AlertTriangle className="h-3 w-3" />
                        {overdueCount} {tMaintenance('overdue')}
                      </span>
                    )}
                    {pendingCount > 0 && overdueCount === 0 && (
                      <span className="inline-flex items-center gap-1 px-3 py-1 bg-orange-100 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300 rounded-full text-xs font-semibold">
                        <Clock className="h-3 w-3" />
                        {pendingCount} {tMaintenance('pending')}
                      </span>
                    )}
                    {overdueCount === 0 && pendingCount === 0 && totalTypes > 0 && (
                      <span className="inline-flex items-center gap-1 px-3 py-1 bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-300 rounded-full text-xs font-semibold">
                        <Check className="h-3 w-3" />
                        {tMaintenance('upToDate')}
                      </span>
                    )}
                  </div>
                  <div className="flex flex-wrap items-center gap-x-2 gap-y-1 mt-2 text-sm text-gray-500 dark:text-gray-400">
                    {device.brand && device.model ? (
                      <span className="font-medium text-gray-700 dark:text-gray-300">
                        {device.brand} {device.model}
                      </span>
                    ) : (
                      <span>{device.type}</span>
                    )}
                    {device.installDate && (
                      <>
                        <span className="text-gray-300 dark:text-gray-600">•</span>
                        <span>{t('installedIn')} {format(new Date(device.installDate), 'MMM yyyy')}</span>
                      </>
                    )}
                  </div>
                </div>
              </div>

              {/* Actions + Score ring */}
              <div className="flex items-center gap-3">
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => setShowEditDialog(true)}
                  title={t('editDevice')}
                >
                  <Pencil className="h-4 w-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => setShowDeleteDialog(true)}
                  title={t('deleteDevice')}
                  className="text-red-600 hover:text-red-700 hover:bg-red-50 dark:hover:bg-red-900/20"
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
                <div className="hidden sm:block">
                  <ScoreRing score={deviceScore} size="md" />
                </div>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Two column layout - responsive */}
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* Left column: Maintenance Types */}
          <div className="lg:col-span-1 space-y-6">
            <div className="flex items-center justify-between">
              <h2 className="text-lg font-bold text-gray-900 dark:text-white">
                {t('maintenanceTypes')}
              </h2>
              <Button
                variant="ghost"
                size="sm"
                className="text-blue-600 hover:text-blue-700"
                onClick={() => setShowAddTypeDialog(true)}
              >
                <Plus className="h-4 w-4 mr-1" />
                {t('addType')}
              </Button>
            </div>

            <div className="space-y-3">
              {maintenanceTypes.length === 0 ? (
                <EmptyState
                  icon={Wrench}
                  title={t('noMaintenanceTypes')}
                  description={t('noMaintenanceTypesDescription')}
                />
              ) : (
                maintenanceTypes.map((type) => (
                  <Card
                    key={type.id}
                    className={`bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm transition-all hover:shadow-lg ${
                      type.status === 'overdue'
                        ? 'border-red-200 dark:border-red-800'
                        : type.status === 'pending'
                        ? 'border-orange-200 dark:border-orange-800'
                        : 'border-white/50'
                    }`}
                  >
                    <CardContent className="p-5">
                      <div className="flex items-start justify-between mb-3">
                        <div className="flex-1">
                          <div className="flex items-center gap-2">
                            <h3 className="font-semibold text-gray-900 dark:text-white">
                              {type.name}
                            </h3>
                            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold ${
                              type.status === 'up_to_date'
                                ? 'bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-300'
                                : type.status === 'pending'
                                ? 'bg-orange-100 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300'
                                : 'bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-300'
                            }`}>
                              {type.status === 'up_to_date' && <Check className="h-3 w-3" />}
                              {type.status === 'up_to_date' ? tMaintenance('upToDate') :
                               type.status === 'pending' ? tMaintenance('pending') : tMaintenance('overdue')}
                            </span>
                          </div>
                          <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
                            {type.periodicity}
                          </p>
                        </div>
                      </div>

                      {/* Progress bar */}
                      <div className="mb-3">
                        <div className="h-2 bg-gray-100 dark:bg-gray-700 rounded-full overflow-hidden">
                          <div
                            className={`h-full rounded-full transition-all ${
                              type.status === 'up_to_date'
                                ? 'bg-gradient-to-r from-green-400 to-emerald-500'
                                : type.status === 'pending'
                                ? 'bg-gradient-to-r from-orange-400 to-amber-400'
                                : 'bg-gradient-to-r from-red-400 to-rose-500'
                            }`}
                            style={{ width: type.status === 'up_to_date' ? '100%' : '0%' }}
                          />
                        </div>
                      </div>

                      <div className="flex items-center justify-between">
                        <span className="text-sm text-gray-500 dark:text-gray-400">
                          {type.nextDueDate
                            ? `${tMaintenance('nextDue')}: ${format(new Date(type.nextDueDate), 'MMM yyyy')}`
                            : ''}
                        </span>
                        <Button
                          size="sm"
                          onClick={() => handleLogMaintenance(type.id)}
                          className={type.status !== 'up_to_date'
                            ? 'bg-gradient-to-r from-blue-500 to-blue-600 hover:from-blue-600 hover:to-blue-700 shadow-lg shadow-blue-500/20'
                            : ''
                          }
                          variant={type.status === 'up_to_date' ? 'ghost' : 'default'}
                        >
                          {tMaintenance('logMaintenance')}
                        </Button>
                      </div>
                    </CardContent>
                  </Card>
                ))
              )}
            </div>

            {/* Stats card */}
            {maintenanceHistory && maintenanceHistory.count > 0 && (
              <Card className="bg-gradient-to-br from-blue-500 to-purple-600 text-white border-0">
                <CardContent className="p-5">
                  <h3 className="font-semibold mb-3">{tMaintenance('statistics')}</h3>
                  <div className="space-y-3">
                    <div className="flex justify-between items-center">
                      <span className="text-blue-100">{tCommon('totalSpent')}</span>
                      <span className="font-bold text-lg">
                        {maintenanceHistory.totalSpent ? `${maintenanceHistory.totalSpent.toFixed(0)} €` : '0 €'}
                      </span>
                    </div>
                    <div className="flex justify-between items-center">
                      <span className="text-blue-100">{tMaintenance('maintenanceLogged')}</span>
                      <span className="font-bold text-lg">{maintenanceHistory.count}</span>
                    </div>
                    {device.installDate && (
                      <div className="flex justify-between items-center">
                        <span className="text-blue-100">{tMaintenance('since')}</span>
                        <span className="font-bold">{format(new Date(device.installDate), 'MMM yyyy')}</span>
                      </div>
                    )}
                  </div>
                </CardContent>
              </Card>
            )}
          </div>

          {/* Right column: History Timeline */}
          <div className="lg:col-span-2">
            <h2 className="text-lg font-bold text-gray-900 dark:text-white mb-4">
              {tMaintenance('history')}
            </h2>

            <Card className="bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm border-white/50">
              <CardContent className="p-6">
                {historyLoading ? (
                  <div className="space-y-6">
                    <ListItemSkeleton />
                    <ListItemSkeleton />
                    <ListItemSkeleton />
                  </div>
                ) : !maintenanceHistory?.instances || maintenanceHistory.instances.length === 0 ? (
                  <EmptyState
                    variant="inline"
                    icon={Clock}
                    title={tMaintenance('noHistory')}
                    description={tMaintenance('noHistoryDescription')}
                  />
                ) : (
                  <div className="space-y-6">
                    {[...maintenanceHistory.instances]
                      .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
                      .map((instance, index) => (
                        <div key={instance.id} className="relative flex gap-4">
                          {/* Timeline dot */}
                          <div className="relative flex-shrink-0">
                            <div className="w-6 h-6 bg-gradient-to-br from-green-400 to-emerald-500 rounded-full flex items-center justify-center shadow-lg shadow-green-500/30">
                              <Check className="h-3 w-3 text-white" />
                            </div>
                            {/* Timeline line */}
                            {index < maintenanceHistory.instances.length - 1 && (
                              <div className="absolute left-[11px] top-8 bottom-0 w-0.5 h-[calc(100%+8px)] bg-gradient-to-b from-gray-200 to-transparent dark:from-gray-700" />
                            )}
                          </div>

                          {/* Content */}
                          <div className="flex-1 bg-gray-50 dark:bg-gray-700/50 rounded-2xl p-4 hover:bg-gray-100 dark:hover:bg-gray-700 transition">
                            <div className="flex flex-wrap items-start justify-between gap-2">
                              <div>
                                <h4 className="font-semibold text-gray-900 dark:text-white">
                                  {instance.maintenanceTypeName}
                                </h4>
                                {instance.provider && (
                                  <p className="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
                                    {instance.provider}
                                  </p>
                                )}
                                {instance.notes && (
                                  <p className="text-sm text-gray-500 dark:text-gray-400 mt-2 italic">
                                    &quot;{instance.notes}&quot;
                                  </p>
                                )}
                              </div>
                              <div className="text-right">
                                {instance.cost && (
                                  <span className="font-bold text-gray-900 dark:text-white">
                                    {instance.cost} €
                                  </span>
                                )}
                                <p className="text-xs text-gray-400">
                                  {format(new Date(instance.date), 'd MMM yyyy')}
                                </p>
                              </div>
                            </div>
                          </div>
                        </div>
                      ))}
                  </div>
                )}
              </CardContent>
            </Card>
          </div>
        </div>
      </div>

      {/* Log Maintenance Dialog */}
      {selectedMaintenanceType && (
        <LogMaintenanceDialog
          maintenanceTypeId={selectedMaintenanceType}
          open={showLogDialog}
          onClose={() => {
            setShowLogDialog(false);
            setSelectedMaintenanceType(null);
          }}
        />
      )}

      {/* Add Maintenance Type Dialog */}
      <AddMaintenanceTypeDialog
        deviceId={id}
        open={showAddTypeDialog}
        onClose={() => setShowAddTypeDialog(false)}
      />

      {/* Edit Device Dialog */}
      <EditDeviceDialog
        deviceId={id}
        device={device}
        open={showEditDialog}
        onClose={() => setShowEditDialog(false)}
      />

      {/* Delete Device Dialog */}
      <DeleteDeviceDialog
        deviceId={id}
        deviceName={device.name}
        houseId={device.houseId}
        open={showDeleteDialog}
        onClose={() => setShowDeleteDialog(false)}
      />
    </div>
  );
}
