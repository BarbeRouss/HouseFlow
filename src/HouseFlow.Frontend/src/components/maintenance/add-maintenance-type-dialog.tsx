"use client";

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { useCreateMaintenanceType } from '@/lib/api/hooks';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

type Periodicity = 'Annual' | 'Semestrial' | 'Quarterly' | 'Monthly' | 'Custom';

interface AddMaintenanceTypeDialogProps {
  deviceId: string;
  open: boolean;
  onClose: () => void;
}

const periodicityOptions: { value: Periodicity; labelKey: string }[] = [
  { value: 'Annual', labelKey: 'annual' },
  { value: 'Semestrial', labelKey: 'semestrial' },
  { value: 'Quarterly', labelKey: 'quarterly' },
  { value: 'Monthly', labelKey: 'monthly' },
  { value: 'Custom', labelKey: 'custom' },
];

export function AddMaintenanceTypeDialog({
  deviceId,
  open,
  onClose,
}: AddMaintenanceTypeDialogProps) {
  const t = useTranslations('maintenance');
  const tCommon = useTranslations('common');

  const [name, setName] = useState('');
  const [periodicity, setPeriodicity] = useState<Periodicity>('Annual');
  const [customDays, setCustomDays] = useState('');

  const createMutation = useCreateMaintenanceType(deviceId, {
    onSuccess: () => {
      onClose();
      // Reset form
      setName('');
      setPeriodicity('Annual');
      setCustomDays('');
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    createMutation.mutate({
      name,
      periodicity,
      customDays: periodicity === 'Custom' && customDays ? parseInt(customDays, 10) : null,
    });
  };

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4"
      onClick={onClose}
    >
      <Card
        className="w-full max-w-md relative"
        onClick={(e) => e.stopPropagation()}
      >
        <CardHeader>
          <CardTitle>{t('addMaintenanceType')}</CardTitle>
          <CardDescription>
            {t('addMaintenanceTypeDescription')}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-6">
            {createMutation.isError && (
              <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 px-4 py-3 rounded">
                {tCommon('createError')}
              </div>
            )}

            {/* Name */}
            <div>
              <label htmlFor="name" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                {t('typeName')}
              </label>
              <input
                id="name"
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                placeholder={t('typeNamePlaceholder')}
              />
            </div>

            {/* Periodicity */}
            <div>
              <label htmlFor="periodicity" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                {t('periodicity')}
              </label>
              <select
                id="periodicity"
                value={periodicity}
                onChange={(e) => setPeriodicity(e.target.value as Periodicity)}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
              >
                {periodicityOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {t(option.labelKey)}
                  </option>
                ))}
              </select>
            </div>

            {/* Custom Days (only shown when periodicity is Custom) */}
            {periodicity === 'Custom' && (
              <div>
                <label htmlFor="customDays" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  {t('customDays')}
                </label>
                <input
                  id="customDays"
                  type="number"
                  min="1"
                  value={customDays}
                  onChange={(e) => setCustomDays(e.target.value)}
                  required
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                  placeholder="90"
                />
              </div>
            )}

            <div className="flex gap-4">
              <Button
                type="button"
                variant="outline"
                onClick={onClose}
                className="flex-1"
              >
                {tCommon('cancel')}
              </Button>
              <Button
                type="submit"
                disabled={createMutation.isPending}
                className="flex-1"
              >
                {createMutation.isPending ? tCommon('loading') : tCommon('add')}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}