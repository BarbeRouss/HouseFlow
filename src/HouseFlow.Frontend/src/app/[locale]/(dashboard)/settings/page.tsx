"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useApiKeys, useCreateApiKey, useRevokeApiKey } from "@/lib/api/hooks";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Key,
  Plus,
  Copy,
  Check,
  Trash2,
  AlertTriangle,
  Shield,
  ShieldCheck,
} from "lucide-react";

export default function SettingsPage() {
  const t = useTranslations("settings");
  const tKeys = useTranslations("apiKeys");
  const tCommon = useTranslations("common");

  const { data: apiKeys, isLoading } = useApiKeys();
  const createMutation = useCreateApiKey();
  const revokeMutation = useRevokeApiKey();

  const [showCreateForm, setShowCreateForm] = useState(false);
  const [keyName, setKeyName] = useState("");
  const [keyScope, setKeyScope] = useState("ReadWrite");
  const [createdKey, setCreatedKey] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [revokeId, setRevokeId] = useState<string | null>(null);

  const handleCreate = () => {
    if (!keyName.trim()) return;
    createMutation.mutate(
      { name: keyName.trim(), scope: keyScope },
      {
        onSuccess: (data) => {
          setCreatedKey(data.key);
          setKeyName("");
          setKeyScope("ReadWrite");
          setShowCreateForm(false);
        },
      }
    );
  };

  const handleCopy = async () => {
    if (!createdKey) return;
    await navigator.clipboard.writeText(createdKey);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleRevoke = (id: string) => {
    revokeMutation.mutate(id, {
      onSuccess: () => setRevokeId(null),
    });
  };

  const activeKeyCount = apiKeys?.length ?? 0;
  const limitReached = activeKeyCount >= 5;

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      {/* Page title */}
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-white">
          {t("title")}
        </h1>
      </div>

      {/* Created key banner - shown once after creation */}
      {createdKey && (
        <Card className="border-green-200 dark:border-green-800 bg-green-50 dark:bg-green-950">
          <CardContent className="pt-6">
            <div className="flex items-start gap-3">
              <ShieldCheck className="h-5 w-5 text-green-600 dark:text-green-400 mt-0.5 shrink-0" />
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-green-800 dark:text-green-200">
                  {tKeys("keyCreated")}
                </p>
                <p className="text-sm text-green-700 dark:text-green-300 mt-1">
                  <AlertTriangle className="h-3.5 w-3.5 inline mr-1" />
                  {tKeys("keyCreatedWarning")}
                </p>
                <div className="mt-3 flex items-center gap-2">
                  <code className="flex-1 min-w-0 bg-white dark:bg-gray-900 border border-green-300 dark:border-green-700 rounded px-3 py-2 text-sm font-mono break-all select-all">
                    {createdKey}
                  </code>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={handleCopy}
                    className="shrink-0"
                  >
                    {copied ? (
                      <Check className="h-4 w-4 text-green-600" />
                    ) : (
                      <Copy className="h-4 w-4" />
                    )}
                    <span className="ml-1">
                      {copied ? tKeys("copied") : tKeys("copy")}
                    </span>
                  </Button>
                </div>
                <div className="mt-2 flex justify-end">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setCreatedKey(null)}
                  >
                    {tCommon("close")}
                  </Button>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>
      )}

      {/* API Keys section */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="flex items-center gap-2">
                <Key className="h-5 w-5" />
                {tKeys("title")}
              </CardTitle>
              <CardDescription className="mt-1">
                {tKeys("description")}
              </CardDescription>
            </div>
            {!showCreateForm && !limitReached && (
              <Button
                size="sm"
                onClick={() => setShowCreateForm(true)}
                disabled={createMutation.isPending}
              >
                <Plus className="h-4 w-4 mr-1" />
                {tKeys("create")}
              </Button>
            )}
          </div>
          {limitReached && (
            <p className="text-sm text-amber-600 dark:text-amber-400 mt-2">
              {tKeys("limitReached")}
            </p>
          )}
        </CardHeader>

        <CardContent>
          {/* Create form */}
          {showCreateForm && (
            <div className="mb-6 p-4 border rounded-lg bg-gray-50 dark:bg-gray-800/50 space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                  {tKeys("name")}
                </label>
                <input
                  type="text"
                  value={keyName}
                  onChange={(e) => setKeyName(e.target.value)}
                  placeholder={tKeys("namePlaceholder")}
                  maxLength={100}
                  className="w-full px-3 py-2 rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-900 text-gray-900 dark:text-white text-sm focus:outline-none focus:ring-2 focus:ring-primary"
                  autoFocus
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                  {tKeys("scope")}
                </label>
                <div className="flex gap-3">
                  <label className="flex items-center gap-2 cursor-pointer">
                    <input
                      type="radio"
                      name="scope"
                      value="ReadWrite"
                      checked={keyScope === "ReadWrite"}
                      onChange={(e) => setKeyScope(e.target.value)}
                      className="text-primary"
                    />
                    <ShieldCheck className="h-4 w-4 text-blue-600 dark:text-blue-400" />
                    <span className="text-sm">{tKeys("scopeReadWrite")}</span>
                  </label>
                  <label className="flex items-center gap-2 cursor-pointer">
                    <input
                      type="radio"
                      name="scope"
                      value="ReadOnly"
                      checked={keyScope === "ReadOnly"}
                      onChange={(e) => setKeyScope(e.target.value)}
                      className="text-primary"
                    />
                    <Shield className="h-4 w-4 text-gray-500" />
                    <span className="text-sm">{tKeys("scopeReadOnly")}</span>
                  </label>
                </div>
              </div>
              <div className="flex gap-2 justify-end">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    setShowCreateForm(false);
                    setKeyName("");
                  }}
                >
                  {tCommon("cancel")}
                </Button>
                <Button
                  size="sm"
                  onClick={handleCreate}
                  disabled={!keyName.trim() || createMutation.isPending}
                >
                  {createMutation.isPending
                    ? tCommon("loading")
                    : tKeys("create")}
                </Button>
              </div>
              {createMutation.isError && (
                <p className="text-sm text-red-600 dark:text-red-400">
                  {(createMutation.error as { response?: { data?: { error?: string } } })
                    ?.response?.data?.error || tCommon("error")}
                </p>
              )}
            </div>
          )}

          {/* Keys list */}
          {isLoading ? (
            <div className="space-y-3">
              {[1, 2].map((i) => (
                <div
                  key={i}
                  className="h-16 bg-gray-100 dark:bg-gray-800 rounded-lg animate-pulse"
                />
              ))}
            </div>
          ) : !apiKeys || apiKeys.length === 0 ? (
            <EmptyState
              icon={Key}
              title={tKeys("noKeys")}
              description={tKeys("noKeysDescription")}
            />
          ) : (
            <div className="space-y-3">
              {apiKeys.map((key) => (
                <div
                  key={key.id}
                  className="flex items-center justify-between p-3 border rounded-lg bg-white dark:bg-gray-900"
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-gray-900 dark:text-white text-sm">
                        {key.name}
                      </span>
                      <span
                        className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium ${
                          key.scope === "ReadWrite"
                            ? "bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300"
                            : "bg-gray-100 dark:bg-gray-800 text-gray-600 dark:text-gray-400"
                        }`}
                      >
                        {key.scope === "ReadWrite" ? (
                          <ShieldCheck className="h-3 w-3" />
                        ) : (
                          <Shield className="h-3 w-3" />
                        )}
                        {key.scope === "ReadWrite"
                          ? tKeys("scopeReadWrite")
                          : tKeys("scopeReadOnly")}
                      </span>
                    </div>
                    <div className="flex items-center gap-4 mt-1 text-xs text-gray-500 dark:text-gray-400">
                      <code className="font-mono">{key.prefix}...</code>
                      <span>
                        {tKeys("createdAt")}{" "}
                        {new Date(key.createdAt).toLocaleDateString()}
                      </span>
                      <span>
                        {tKeys("lastUsed")}{" "}
                        {key.lastUsedAt
                          ? new Date(key.lastUsedAt).toLocaleDateString()
                          : tKeys("never")}
                      </span>
                    </div>
                  </div>
                  <div className="shrink-0 ml-3">
                    {revokeId === key.id ? (
                      <div className="flex items-center gap-2">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setRevokeId(null)}
                        >
                          {tCommon("cancel")}
                        </Button>
                        <Button
                          variant="destructive"
                          size="sm"
                          onClick={() => handleRevoke(key.id)}
                          disabled={revokeMutation.isPending}
                        >
                          <Trash2 className="h-3.5 w-3.5 mr-1" />
                          {tKeys("revoke")}
                        </Button>
                      </div>
                    ) : (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => setRevokeId(key.id)}
                        className="text-red-600 dark:text-red-400 hover:text-red-700 dark:hover:text-red-300"
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
