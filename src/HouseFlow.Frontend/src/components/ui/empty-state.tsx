import * as React from "react";
import { Card, CardContent } from "@/components/ui/card";
import { cn } from "@/lib/utils/cn";
import { type LucideIcon } from "lucide-react";

interface EmptyStateProps {
  icon: LucideIcon;
  title: string;
  description: string;
  action?: React.ReactNode;
  className?: string;
  /** Use "inline" when the EmptyState is already inside a Card */
  variant?: "card" | "inline";
}

export function EmptyState({ icon: Icon, title, description, action, className, variant = "card" }: EmptyStateProps) {
  const content = (
    <div className={cn("flex flex-col items-center justify-center py-12 px-6 text-center", variant === "card" && "py-16")}>
      <div className="w-16 h-16 bg-gray-100 dark:bg-gray-700 rounded-full flex items-center justify-center mb-4">
        <Icon className="h-8 w-8 text-gray-400 dark:text-gray-500" />
      </div>
      <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">
        {title}
      </h3>
      <p className="text-sm text-gray-500 dark:text-gray-400 max-w-sm mb-6">
        {description}
      </p>
      {action && <div>{action}</div>}
    </div>
  );

  if (variant === "inline") {
    return <div className={className}>{content}</div>;
  }

  return (
    <Card className={cn("bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm border-dashed border-2 border-gray-200 dark:border-gray-700", className)}>
      <CardContent>
        {content}
      </CardContent>
    </Card>
  );
}
