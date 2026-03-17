"use client";

import { useState } from 'react';
import { useTranslations, useLocale } from 'next-intl';
import {
  useHouseMembers,
  useHouseInvitations,
  useCreateInvitation,
  useRevokeInvitation,
  useUpdateMemberRole,
  useUpdateMemberPermissions,
  useRemoveMember,
} from '@/lib/api/hooks';
import type { HouseMemberDto, InvitationDto } from '@/lib/api/hooks/members';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Users, Copy, Check, Trash2, Shield, Link2, ChevronDown } from 'lucide-react';

const ROLES = ['CollaboratorRW', 'CollaboratorRO', 'Tenant'] as const;

const roleTranslationKey: Record<string, string> = {
  Owner: 'owner',
  CollaboratorRW: 'collaboratorRW',
  CollaboratorRO: 'collaboratorRO',
  Tenant: 'tenant',
};

interface MembersSectionProps {
  houseId: string;
  userRole: string;
}

export function MembersSection({ houseId, userRole }: MembersSectionProps) {
  const locale = useLocale();
  const t = useTranslations('houses');
  const tCommon = useTranslations('common');

  const isOwner = userRole === 'Owner';
  const isCollaboratorRW = userRole === 'CollaboratorRW';
  const canManage = isOwner || isCollaboratorRW;

  const { data: members = [] } = useHouseMembers(houseId);
  const { data: invitations = [] } = useHouseInvitations(houseId, { enabled: canManage });

  const [selectedRole, setSelectedRole] = useState<string>('CollaboratorRW');
  const [copiedLink, setCopiedLink] = useState<string | null>(null);
  const [showRoleMenu, setShowRoleMenu] = useState<string | null>(null);

  const createInvitation = useCreateInvitation(houseId);
  const revokeInvitation = useRevokeInvitation(houseId);
  const updateRole = useUpdateMemberRole();
  const updatePermissions = useUpdateMemberPermissions();
  const removeMember = useRemoveMember();

  const handleCreateInvitation = () => {
    createInvitation.mutate({ role: selectedRole });
  };

  const handleCopyLink = (token: string) => {
    const url = `${window.location.origin}/${locale}/invitations/${token}`;
    navigator.clipboard.writeText(url);
    setCopiedLink(token);
    setTimeout(() => setCopiedLink(null), 2000);
  };

  const handleRemoveMember = (member: HouseMemberDto) => {
    if (confirm(t('removeMemberConfirmation', { name: `${member.firstName} ${member.lastName}` }))) {
      removeMember.mutate(member.id);
    }
  };

  // Owner can invite any role; CollaboratorRW can only invite Tenant
  const availableRoles = isOwner ? ROLES : isCollaboratorRW ? (['Tenant'] as const) : [];

  return (
    <Card className="bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm border-white/50">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Users className="h-5 w-5" />
          {t('manageMembers')}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-6">
        {/* Members list */}
        <div className="space-y-3">
          {members.map((member) => (
            <div key={member.id} className="flex items-center justify-between p-3 bg-gray-50 dark:bg-gray-700/50 rounded-lg">
              <div className="flex items-center gap-3 min-w-0">
                <div className="w-10 h-10 bg-gradient-to-br from-blue-100 to-blue-200 dark:from-blue-900/30 dark:to-blue-800/30 rounded-full flex items-center justify-center flex-shrink-0">
                  <span className="text-sm font-bold text-blue-600 dark:text-blue-400">
                    {member.firstName[0]}{member.lastName[0]}
                  </span>
                </div>
                <div className="min-w-0">
                  <p className="font-medium text-gray-900 dark:text-white truncate">
                    {member.firstName} {member.lastName}
                  </p>
                  <p className="text-xs text-gray-500 dark:text-gray-400 truncate">{member.email}</p>
                </div>
              </div>

              <div className="flex items-center gap-2 flex-shrink-0">
                {/* Role badge */}
                <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-medium ${
                  member.role === 'Owner'
                    ? 'bg-purple-100 dark:bg-purple-900/30 text-purple-700 dark:text-purple-300'
                    : member.role === 'Tenant'
                    ? 'bg-gray-100 dark:bg-gray-600/50 text-gray-700 dark:text-gray-300'
                    : 'bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300'
                }`}>
                  <Shield className="h-3 w-3" />
                  {t(roleTranslationKey[member.role] || 'collaboratorRO')}
                </span>

                {/* Role change dropdown for non-owner members (only if current user is owner) */}
                {isOwner && member.role !== 'Owner' && (
                  <div className="relative">
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8"
                      onClick={() => setShowRoleMenu(showRoleMenu === member.id ? null : member.id)}
                    >
                      <ChevronDown className="h-4 w-4" />
                    </Button>
                    {showRoleMenu === member.id && (
                      <div className="absolute right-0 top-full mt-1 w-48 bg-white dark:bg-gray-800 border dark:border-gray-700 rounded-lg shadow-lg z-10">
                        {ROLES.map((role) => (
                          <button
                            key={role}
                            className="w-full text-left px-4 py-2 text-sm hover:bg-gray-100 dark:hover:bg-gray-700 first:rounded-t-lg last:rounded-b-lg"
                            onClick={() => {
                              updateRole.mutate({ memberId: member.id, role });
                              setShowRoleMenu(null);
                            }}
                          >
                            {t(roleTranslationKey[role])}
                          </button>
                        ))}
                        {member.role === 'Tenant' && (
                          <div className="border-t dark:border-gray-700 px-4 py-2">
                            <label className="flex items-center gap-2 text-sm cursor-pointer">
                              <input
                                type="checkbox"
                                checked={member.canLogMaintenance}
                                onChange={(e) => {
                                  updatePermissions.mutate({
                                    memberId: member.id,
                                    canLogMaintenance: e.target.checked,
                                  });
                                }}
                                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                              />
                              {t('canLogMaintenance')}
                            </label>
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )}

                {/* Remove button for non-owner members */}
                {canManage && member.role !== 'Owner' && (
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 text-red-600 hover:text-red-700 hover:bg-red-50 dark:hover:bg-red-900/20"
                    onClick={() => handleRemoveMember(member)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                )}
              </div>
            </div>
          ))}
        </div>

        {/* Create invitation section (only for users who can manage) */}
        {canManage && availableRoles.length > 0 && (
          <div className="border-t dark:border-gray-700 pt-4">
            <h4 className="text-sm font-semibold text-gray-900 dark:text-white mb-3 flex items-center gap-2">
              <Link2 className="h-4 w-4" />
              {t('createInvitation')}
            </h4>
            <div className="flex gap-2">
              <select
                value={selectedRole}
                onChange={(e) => setSelectedRole(e.target.value)}
                className="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white text-sm"
              >
                {availableRoles.map((role) => (
                  <option key={role} value={role}>{t(roleTranslationKey[role])}</option>
                ))}
              </select>
              <Button
                onClick={handleCreateInvitation}
                disabled={createInvitation.isPending}
                className="bg-gradient-to-r from-blue-500 to-blue-600 hover:from-blue-600 hover:to-blue-700"
              >
                {createInvitation.isPending ? tCommon('loading') : t('createInvitation')}
              </Button>
            </div>
          </div>
        )}

        {/* Pending invitations */}
        {canManage && invitations.length > 0 && (
          <div className="border-t dark:border-gray-700 pt-4">
            <h4 className="text-sm font-semibold text-gray-900 dark:text-white mb-3">
              {t('pendingInvitations')} ({invitations.length})
            </h4>
            <div className="space-y-2">
              {invitations.map((invitation) => (
                <div key={invitation.id} className="flex items-center justify-between p-3 bg-yellow-50 dark:bg-yellow-900/10 border border-yellow-200 dark:border-yellow-800/30 rounded-lg">
                  <div className="min-w-0">
                    <span className="text-xs font-medium text-yellow-700 dark:text-yellow-300">
                      {t(roleTranslationKey[invitation.role] || 'collaboratorRO')}
                    </span>
                    <p className="text-xs text-gray-500 dark:text-gray-400">
                      {t('expiresOn', { date: new Date(invitation.expiresAt).toLocaleDateString(locale) })}
                    </p>
                  </div>
                  <div className="flex items-center gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8"
                      onClick={() => handleCopyLink(invitation.token)}
                      title={t('copyLink')}
                    >
                      {copiedLink === invitation.token ? (
                        <Check className="h-4 w-4 text-green-600" />
                      ) : (
                        <Copy className="h-4 w-4" />
                      )}
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 text-red-600 hover:text-red-700 hover:bg-red-50 dark:hover:bg-red-900/20"
                      onClick={() => revokeInvitation.mutate(invitation.id)}
                      title={t('revokeInvitation')}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
