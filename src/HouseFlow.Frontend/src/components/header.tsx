"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { LogOut } from "lucide-react";
import { Logo } from "@/components/logo";
import { useAuth } from "@/lib/auth/context";
import { Button } from "@/components/ui/button";
import { ThemeToggle } from "@/components/theme-toggle";
import { LocaleSwitcher } from "@/components/locale-switcher";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

export function Header() {
  const locale = useLocale();
  const t = useTranslations("header");
  const tAuth = useTranslations("auth");
  const { user, logout } = useAuth();

  return (
    <header className="sticky top-0 z-40 w-full border-b bg-white/80 dark:bg-gray-900/80 backdrop-blur-sm">
      <div className="max-w-7xl mx-auto flex h-14 items-center justify-between px-4 sm:px-8">
        {/* Logo / App name */}
        <Link
          href={`/${locale}/dashboard`}
          className="flex items-center gap-2 font-bold text-lg text-gray-900 dark:text-white hover:opacity-80 transition"
        >
          <Logo size={28} />
          <span>HouseFlow</span>
        </Link>

        {/* Right side: theme toggle + user menu */}
        <div className="flex items-center gap-1">
          <LocaleSwitcher />
          <ThemeToggle />

          {user && (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  className="flex items-center gap-2 px-3"
                >
                  <div className="h-7 w-7 rounded-full bg-primary/10 flex items-center justify-center text-sm font-semibold text-primary">
                    {user.firstName.charAt(0)}
                    {user.lastName.charAt(0)}
                  </div>
                  <span className="hidden sm:inline text-sm font-medium text-gray-700 dark:text-gray-300">
                    {user.firstName}
                  </span>
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-48">
                <DropdownMenuLabel>
                  {user.firstName} {user.lastName}
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  onClick={() => logout()}
                  className="text-red-600 dark:text-red-400 cursor-pointer"
                >
                  <LogOut className="mr-2 h-4 w-4" />
                  {tAuth("logout")}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          )}
        </div>
      </div>
    </header>
  );
}
