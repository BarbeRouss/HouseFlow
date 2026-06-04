import { NextRequest } from 'next/server';
import createIntlMiddleware from 'next-intl/middleware';
import { locales, defaultLocale } from './lib/i18n/config';
import { buildCspHeader } from './lib/csp';

const intlMiddleware = createIntlMiddleware({
  locales,
  defaultLocale,
  localePrefix: 'always',
});

export default function middleware(request: NextRequest) {
  // Generate a cryptographic nonce for this request
  const nonce = Buffer.from(crypto.randomUUID()).toString('base64');

  // Build CSP header with the nonce
  const isDev = process.env.NODE_ENV === 'development';
  const cspHeader = buildCspHeader(nonce, isDev);

  // Run intl middleware (handles locale routing, redirects, rewrites)
  const response = intlMiddleware(request);

  // Set CSP header on response
  response.headers.set('Content-Security-Policy', cspHeader);

  // Pass nonce to server components via a short-lived cookie.
  // Using x-middleware-override-headers breaks intl routing, so we use cookies
  // instead — they're available in server components via cookies() and don't
  // interfere with Next.js internal routing mechanisms.
  response.cookies.set('__csp_nonce', nonce, {
    httpOnly: true,
    sameSite: 'strict',
    path: '/',
  });

  return response;
}

export const config = {
  // Match only internationalized pathnames
  matcher: ['/', '/(fr|en)/:path*'],
};
