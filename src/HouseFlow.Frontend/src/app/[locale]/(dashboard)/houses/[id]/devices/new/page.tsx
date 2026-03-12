"use client";

import { use, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations, useLocale } from 'next-intl';
import { useCreateDevice } from '@/lib/api/hooks';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

export default function NewDevicePage({ params }: { params: Promise<{ id: string }> }) {
  const { id: houseId } = use(params);
  const router = useRouter();
  const locale = useLocale();
  const t = useTranslations('devices');
  const tCommon = useTranslations('common');

  const [name, setName] = useState('');
  const [type, setType] = useState('');
  const [brand, setBrand] = useState('');
  const [model, setModel] = useState('');
  const [installDate, setInstallDate] = useState('');

  const createDeviceMutation = useCreateDevice(houseId);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    createDeviceMutation.mutate({
      name,
      type,
      brand: brand || null,
      model: model || null,
      installDate: installDate || null,
    }, {
      onSuccess: () => {
        // Navigate immediately - invalidation will complete in the background
        router.push(`/${locale}/houses/${houseId}`);
      },
    });
  };

  const deviceTypes = [
    'Chaudière Gaz',
    'Chaudière Fioul',
    'Pompe à Chaleur',
    'Climatisation',
    'Poêle à Bois',
    'Chauffe-eau',
    'VMC',
    'Toiture',
    'Détecteur de Fumée',
    'Alarme',
    'Autre',
  ];

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-gray-900 p-8">
      <div className="max-w-2xl mx-auto">
        <Card>
          <CardHeader>
            <CardTitle>{t('addDevice')}</CardTitle>
            <CardDescription>
              {t('noDevicesDescription')}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit} className="space-y-6">
              {createDeviceMutation.isError && (
                <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 px-4 py-3 rounded">
                  {t('createError')}
                </div>
              )}

              <div>
                <label htmlFor="name" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  {t('deviceName')}
                </label>
                <input
                  id="name"
                  type="text"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  required
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                  placeholder="Chaudière Sous-sol"
                />
              </div>

              <div>
                <label htmlFor="type" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  {t('deviceType')}
                </label>
                <select
                  id="type"
                  value={type}
                  onChange={(e) => setType(e.target.value)}
                  required
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                >
                  <option value="">{tCommon('selectType')}</option>
                  {deviceTypes.map((deviceType) => (
                    <option key={deviceType} value={deviceType}>
                      {deviceType}
                    </option>
                  ))}
                </select>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label htmlFor="brand" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                    {t('brand')} ({tCommon('optional')})
                  </label>
                  <input
                    id="brand"
                    type="text"
                    value={brand}
                    onChange={(e) => setBrand(e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                    placeholder="Viessmann"
                  />
                </div>
                <div>
                  <label htmlFor="model" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                    {t('model')} ({tCommon('optional')})
                  </label>
                  <input
                    id="model"
                    type="text"
                    value={model}
                    onChange={(e) => setModel(e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                    placeholder="Vitodens 200"
                  />
                </div>
              </div>

              <div>
                <label htmlFor="installDate" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  {t('installDate')} ({tCommon('optional')})
                </label>
                <input
                  id="installDate"
                  type="date"
                  value={installDate}
                  onChange={(e) => setInstallDate(e.target.value)}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                />
              </div>

              <div className="flex gap-4">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => router.back()}
                  className="flex-1"
                >
                  {tCommon('cancel')}
                </Button>
                <Button
                  type="submit"
                  disabled={createDeviceMutation.isPending}
                  className="flex-1"
                >
                  {createDeviceMutation.isPending ? tCommon('loading') : tCommon('save')}
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
