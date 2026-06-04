import { NextIntlClientProvider } from 'next-intl';
import { getMessages } from 'next-intl/server';
import { cookies } from 'next/headers';
import { notFound } from 'next/navigation';
import { locales } from '@/lib/i18n/config';
import { ThemeProvider } from '@/components/providers/theme-provider';
import { QueryProvider } from '@/components/providers/query-provider';
import { AuthProvider } from '@/lib/auth/context';
import { RetryIndicator } from '@/components/ui/retry-indicator';
import "../globals.css";

export default async function LocaleLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  // Ensure that the incoming `locale` is valid
  if (!locales.includes(locale as any)) {
    notFound();
  }

  // Providing all messages to the client
  // side is the easiest way to get started
  const messages = await getMessages();

  // Read CSP nonce from middleware (passed via cookie to avoid
  // x-middleware-override-headers which breaks intl routing)
  const cookieStore = await cookies();
  const nonce = cookieStore.get('__csp_nonce')?.value ?? '';

  // Runtime API URL injection — use API_URL (not NEXT_PUBLIC_*) to avoid build-time inlining
  const apiUrl = process.env.API_URL || process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5203';
  const demoMode = process.env.DEMO_MODE === 'true';

  return (
    <html lang={locale} suppressHydrationWarning>
      <head>
        <script
          nonce={nonce}
          suppressHydrationWarning
          dangerouslySetInnerHTML={{
            __html: `window.__RUNTIME_CONFIG__ = { API_URL: ${JSON.stringify(apiUrl)}, DEMO_MODE: ${JSON.stringify(demoMode)} };`,
          }}
        />
      </head>
      <body className="font-sans antialiased">
        <NextIntlClientProvider messages={messages}>
          <ThemeProvider
            attribute="class"
            defaultTheme="system"
            enableSystem
            disableTransitionOnChange
            nonce={nonce}
          >
            <QueryProvider>
              <AuthProvider>
                {children}
                <RetryIndicator />
              </AuthProvider>
            </QueryProvider>
          </ThemeProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
