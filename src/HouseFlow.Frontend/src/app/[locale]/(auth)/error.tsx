"use client";

import { useEffect } from "react";
import { AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useTranslations } from "next-intl";
import { logClientError } from "@/lib/error-logger";

export default function AuthError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  const t = useTranslations("errors");

  useEffect(() => {
    logClientError(error, "route-error", { digest: error.digest });
  }, [error]);

  return (
    <div className="min-h-screen flex items-center justify-center p-8">
      <Card className="max-w-md w-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm">
        <CardContent className="p-8 text-center">
          <div className="w-16 h-16 bg-red-100 dark:bg-red-900/30 rounded-full flex items-center justify-center mx-auto mb-4">
            <AlertTriangle className="h-8 w-8 text-red-600 dark:text-red-400" />
          </div>
          <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">
            {t("somethingWentWrong")}
          </h3>
          <p className="text-sm text-gray-500 dark:text-gray-400 mb-6">
            {t("authError")}
          </p>
          <Button
            onClick={reset}
            className="bg-gradient-to-r from-blue-500 to-blue-600 hover:from-blue-600 hover:to-blue-700"
          >
            {t("tryAgain")}
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
