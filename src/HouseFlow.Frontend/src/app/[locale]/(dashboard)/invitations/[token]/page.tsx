"use client";

import { use, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations, useLocale } from 'next-intl';
import { useInvitationInfo, useAcceptInvitation } from '@/lib/api/hooks';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { LoadingSpinner } from '@/components/ui/loading-spinner';
import { Home, Users, Shield, Check, AlertTriangle } from 'lucide-react';

const roleLabels: Record<string, string> = {
  CollaboratorRW: 'collaboratorRW',
  CollaboratorRO: 'collaboratorRO',
  Tenant: 'tenant',
};

export default function AcceptInvitationPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = use(params);
  const locale = useLocale();
  const router = useRouter();
  const t = useTranslations('invitations');
  const tHouses = useTranslations('houses');

  const [accepted, setAccepted] = useState(false);

  const { data: info, isLoading, isError } = useInvitationInfo(token);
  const acceptMutation = useAcceptInvitation({
    onSuccess: (data) => {
      setAccepted(true);
      setTimeout(() => {
        router.push(`/${locale}/houses/${data.houseId}`);
      }, 1500);
    },
  });

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900">
        <LoadingSpinner />
      </div>
    );
  }

  if (isError || !info) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900 p-4">
        <Card className="max-w-md w-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm">
          <CardContent className="p-8 text-center">
            <div className="w-16 h-16 bg-red-100 dark:bg-red-900/30 rounded-full flex items-center justify-center mx-auto mb-4">
              <AlertTriangle className="h-8 w-8 text-red-600 dark:text-red-400" />
            </div>
            <h2 className="text-xl font-bold text-gray-900 dark:text-white mb-2">{t('notFound')}</h2>
            <p className="text-gray-500 dark:text-gray-400">{t('invalidOrExpired')}</p>
            <Button
              className="mt-6"
              onClick={() => router.push(`/${locale}/dashboard`)}
            >
              {tHouses('title')}
            </Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  if (info.isExpired) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900 p-4">
        <Card className="max-w-md w-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm">
          <CardContent className="p-8 text-center">
            <div className="w-16 h-16 bg-orange-100 dark:bg-orange-900/30 rounded-full flex items-center justify-center mx-auto mb-4">
              <AlertTriangle className="h-8 w-8 text-orange-600 dark:text-orange-400" />
            </div>
            <h2 className="text-xl font-bold text-gray-900 dark:text-white mb-2">{t('expired')}</h2>
            <p className="text-gray-500 dark:text-gray-400">{t('invalidOrExpired')}</p>
          </CardContent>
        </Card>
      </div>
    );
  }

  const roleKey = roleLabels[info.role] || 'collaboratorRO';

  return (
    <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 via-blue-50/30 to-slate-50 dark:from-gray-900 dark:via-gray-900 dark:to-gray-900 p-4">
      <Card className="max-w-md w-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm">
        <CardHeader className="text-center pb-2">
          <div className="w-16 h-16 bg-gradient-to-br from-blue-100 to-blue-200 dark:from-blue-900/30 dark:to-blue-800/30 rounded-full flex items-center justify-center mx-auto mb-4">
            <Users className="h-8 w-8 text-blue-600 dark:text-blue-400" />
          </div>
          <CardTitle className="text-xl">{t('acceptTitle')}</CardTitle>
          <p className="text-sm text-gray-500 dark:text-gray-400">{t('acceptDescription')}</p>
        </CardHeader>
        <CardContent className="space-y-4">
          {accepted ? (
            <div className="text-center py-4">
              <div className="w-16 h-16 bg-green-100 dark:bg-green-900/30 rounded-full flex items-center justify-center mx-auto mb-4">
                <Check className="h-8 w-8 text-green-600 dark:text-green-400" />
              </div>
              <p className="font-semibold text-green-600 dark:text-green-400 mb-1">{t('accepted')}</p>
              <p className="text-sm text-gray-500 dark:text-gray-400">{t('redirecting')}</p>
            </div>
          ) : (
            <>
              <div className="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 space-y-3">
                <div className="flex items-center gap-3">
                  <Home className="h-5 w-5 text-gray-400" />
                  <div>
                    <p className="text-xs text-gray-500 dark:text-gray-400">{t('houseName')}</p>
                    <p className="font-semibold text-gray-900 dark:text-white">{info.houseName}</p>
                  </div>
                </div>
                <div className="flex items-center gap-3">
                  <Shield className="h-5 w-5 text-gray-400" />
                  <div>
                    <p className="text-xs text-gray-500 dark:text-gray-400">{t('role')}</p>
                    <p className="font-semibold text-gray-900 dark:text-white">{tHouses(roleKey)}</p>
                  </div>
                </div>
                <div className="flex items-center gap-3">
                  <Users className="h-5 w-5 text-gray-400" />
                  <div>
                    <p className="text-xs text-gray-500 dark:text-gray-400">{t('invitedBy')}</p>
                    <p className="font-semibold text-gray-900 dark:text-white">{info.invitedByName}</p>
                  </div>
                </div>
              </div>

              {acceptMutation.isError && (
                <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 px-4 py-3 rounded text-sm">
                  {t('error')}
                </div>
              )}

              <Button
                className="w-full bg-gradient-to-r from-blue-500 to-blue-600 hover:from-blue-600 hover:to-blue-700 shadow-lg shadow-blue-500/30"
                onClick={() => acceptMutation.mutate(token)}
                disabled={acceptMutation.isPending}
              >
                {acceptMutation.isPending ? t('accepting') : t('accept')}
              </Button>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
