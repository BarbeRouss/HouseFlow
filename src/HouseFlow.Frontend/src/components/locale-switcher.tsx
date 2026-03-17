"use client";

import { useLocale } from "next-intl";
import { useRouter, usePathname } from "next/navigation";
import { useTheme } from "next-themes";
import { Globe, Check } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { locales, localeNames, type Locale } from "@/lib/i18n/config";
import { useAuth } from "@/lib/auth/context";
import { useUpdateUserSettings, useUserSettings } from "@/lib/api/hooks/user-settings";

export function LocaleSwitcher() {
  const locale = useLocale();
  const router = useRouter();
  const pathname = usePathname();
  const { theme } = useTheme();
  const { isAuthenticated } = useAuth();
  const { data: settings } = useUserSettings();
  const { mutate: updateSettings } = useUpdateUserSettings();

  const switchLocale = (newLocale: Locale) => {
    if (newLocale === locale) return;

    if (isAuthenticated) {
      updateSettings({ theme: settings?.theme ?? theme ?? "system", language: newLocale });
    }

    const newPath = pathname.replace(`/${locale}`, `/${newLocale}`);
    router.push(newPath);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="relative">
          <Globe className="h-5 w-5" />
          <span className="sr-only">Language</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {locales.map((l) => (
          <DropdownMenuItem
            key={l}
            onClick={() => switchLocale(l)}
          >
            {localeNames[l]}
            {locale === l && <Check className="ml-auto h-4 w-4" />}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
