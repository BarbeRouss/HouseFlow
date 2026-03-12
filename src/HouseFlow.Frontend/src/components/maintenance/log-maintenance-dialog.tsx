"use client";

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { useLogMaintenance } from '@/lib/api/hooks';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

interface LogMaintenanceDialogProps {
  maintenanceTypeId: string;
  open: boolean;
  onClose: () => void;
}

export function LogMaintenanceDialog({
  maintenanceTypeId,
  open,
  onClose,
}: LogMaintenanceDialogProps) {
  const t = useTranslations('maintenance');
  const tCommon = useTranslations('common');

  const [mode, setMode] = useState<'quick' | 'detailed'>('quick');
  const [date, setDate] = useState(new Date().toISOString().split('T')[0]);
  const [cost, setCost] = useState('');
  const [provider, setProvider] = useState('');
  const [notes, setNotes] = useState('');

  const logMaintenanceMutation = useLogMaintenance(maintenanceTypeId, {
    onSuccess: () => {
      onClose();
      // Reset form
      setDate(new Date().toISOString().split('T')[0]);
      setCost('');
      setProvider('');
      setNotes('');
      setMode('quick');
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    logMaintenanceMutation.mutate({
      date: new Date(date).toISOString(),
      cost: mode === 'detailed' && cost ? parseFloat(cost) : null,
      provider: mode === 'detailed' ? provider || null : null,
      notes: mode === 'detailed' ? notes || null : null,
    });
  };

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4"
      onClick={onClose}
    >
      <Card
        className="w-full max-w-2xl relative"
        onClick={(e) => e.stopPropagation()}
      >
        <CardHeader>
          <CardTitle>{t('logMaintenance')}</CardTitle>
          <CardDescription>
            Record maintenance for this device
          </CardDescription>
        </CardHeader>
        <CardContent>
          {/* Mode Selection */}
          <div className="flex gap-2 mb-6">
            <Button
              type="button"
              variant={mode === 'quick' ? 'default' : 'outline'}
              onClick={() => setMode('quick')}
              className="flex-1"
            >
              {t('quickLog')}
            </Button>
            <Button
              type="button"
              variant={mode === 'detailed' ? 'default' : 'outline'}
              onClick={() => setMode('detailed')}
              className="flex-1"
            >
              {t('detailedLog')}
            </Button>
          </div>

          <form onSubmit={handleSubmit} className="space-y-6">
            {logMaintenanceMutation.isError && (
              <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 px-4 py-3 rounded">
                Failed to log maintenance. Please try again.
              </div>
            )}

            {/* Date */}
            <div>
              <label htmlFor="date" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                {t('date')}
              </label>
              <input
                id="date"
                type="date"
                value={date}
                onChange={(e) => setDate(e.target.value)}
                required
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
              />
            </div>

            {/* Detailed Fields */}
            {mode === 'detailed' && (
              <>
                <div>
                  <label htmlFor="cost" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                    {t('cost')} (€)
                  </label>
                  <input
                    id="cost"
                    type="number"
                    step="0.01"
                    value={cost}
                    onChange={(e) => setCost(e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                    placeholder="150.00"
                  />
                </div>

                <div>
                  <label htmlFor="provider" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                    {t('provider')}
                  </label>
                  <input
                    id="provider"
                    type="text"
                    value={provider}
                    onChange={(e) => setProvider(e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                    placeholder="Company Name"
                  />
                </div>

                <div>
                  <label htmlFor="notes" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                    {t('notes')}
                  </label>
                  <textarea
                    id="notes"
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    rows={4}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                    placeholder="Any additional notes..."
                  />
                </div>
              </>
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
                disabled={logMaintenanceMutation.isPending}
                className="flex-1"
              >
                {logMaintenanceMutation.isPending ? tCommon('loading') : tCommon('save')}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
