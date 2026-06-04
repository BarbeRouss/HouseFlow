"use client";

import { Component, type ErrorInfo, type ReactNode } from "react";
import { AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useTranslations } from "next-intl";
import { logClientError } from "@/lib/error-logger";

interface ErrorBoundaryClassProps {
  children: ReactNode;
  fallback?: ReactNode;
  translations?: {
    somethingWentWrong: string;
    unexpectedError: string;
    tryAgain: string;
  };
  onReset?: () => void;
}

interface ErrorBoundaryState {
  hasError: boolean;
}

class ErrorBoundaryClass extends Component<ErrorBoundaryClassProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryClassProps) {
    super(props);
    this.state = { hasError: false };
  }

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    logClientError(error, "error-boundary", {
      componentStack: info.componentStack,
    });
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }

      const { translations } = this.props;

      return (
        <div className="min-h-[400px] flex items-center justify-center p-8">
          <Card className="max-w-md w-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm">
            <CardContent className="p-8 text-center">
              <div className="w-16 h-16 bg-red-100 dark:bg-red-900/30 rounded-full flex items-center justify-center mx-auto mb-4">
                <AlertTriangle className="h-8 w-8 text-red-600 dark:text-red-400" />
              </div>
              <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">
                {translations?.somethingWentWrong ?? "Something went wrong"}
              </h3>
              <p className="text-sm text-gray-500 dark:text-gray-400 mb-6">
                {translations?.unexpectedError ?? "An unexpected error occurred. Please try again."}
              </p>
              <Button
                onClick={() => {
                  this.setState({ hasError: false });
                  this.props.onReset?.();
                }}
                className="bg-gradient-to-r from-blue-500 to-blue-600 hover:from-blue-600 hover:to-blue-700"
              >
                {translations?.tryAgain ?? "Try again"}
              </Button>
            </CardContent>
          </Card>
        </div>
      );
    }

    return this.props.children;
  }
}

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
  onReset?: () => void;
}

export function ErrorBoundary({ children, fallback, onReset }: ErrorBoundaryProps) {
  const t = useTranslations("common");

  return (
    <ErrorBoundaryClass
      fallback={fallback}
      onReset={onReset}
      translations={{
        somethingWentWrong: t("somethingWentWrong"),
        unexpectedError: t("unexpectedError"),
        tryAgain: t("tryAgain"),
      }}
    >
      {children}
    </ErrorBoundaryClass>
  );
}
