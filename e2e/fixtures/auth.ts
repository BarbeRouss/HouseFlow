import { test as base, expect, Page, APIResponse, BrowserContext } from '@playwright/test';

const API_URL = process.env.API_URL || process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5203';

/**
 * Extract the HttpOnly `refreshToken` cookie value from an auth API response
 * (register / login / refresh). The value is kept URL-encoded, exactly as the
 * server emitted it, so the browser sends it back unchanged.
 */
export function refreshCookieFrom(res: APIResponse): string {
  const header = res
    .headersArray()
    .find((h) => h.name.toLowerCase() === 'set-cookie' && h.value.startsWith('refreshToken='));
  if (!header) throw new Error('No refreshToken cookie in the auth response');
  return header.value.split(';')[0].substring('refreshToken='.length);
}

/** localStorage key telling the app that a session exists and a boot refresh is worth trying. */
export const SESSION_HINT_KEY = 'houseflow_session';

/**
 * Log a browser context in as the user who obtained this refresh cookie. The
 * access token only lives in memory: at boot the app exchanges the cookie for
 * one (App.razor) — but only when the session hint is present, so set both.
 */
export async function addRefreshCookie(context: BrowserContext, refreshCookie: string) {
  await context.addCookies([
    { name: 'refreshToken', value: refreshCookie, url: API_URL, httpOnly: true, sameSite: 'Lax' },
  ]);
  await context.addInitScript((key) => {
    try { localStorage.setItem(key, '1'); } catch { /* storage unavailable */ }
  }, SESSION_HINT_KEY);
}

/**
 * Generate a unique email for test isolation
 */
export function generateTestEmail(): string {
  return `test-${Date.now()}-${Math.random().toString(36).substring(7)}@houseflow.test`;
}

type TestUser = { email: string; password: string; firstName: string; lastName: string };

/**
 * Extended test fixture with authenticated user
 */
export const test = base.extend<{
  authenticatedPage: Page;
  testUser: TestUser;
}>({
  testUser: async ({}, use) => {
    const user: TestUser = {
      email: generateTestEmail(),
      password: 'TestPassword123!', // Updated to meet new requirements: 12+ chars with special char
      firstName: 'Test',
      lastName: 'User',
    };
    await use(user);
  },

  authenticatedPage: async ({ page, testUser }: { page: Page; testUser: TestUser }, use) => {
    const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';

    // Register via the UI form (this ensures proper cookie handling and database consistency)
    await page.goto(`${FRONTEND_URL}/fr/register`);
    await page.waitForLoadState('networkidle');

    // Fill registration form
    await page.getByPlaceholder('Jean').fill(testUser.firstName);
    await page.getByPlaceholder('Dupont').fill(testUser.lastName);
    await page.getByPlaceholder('you@example.com').fill(testUser.email);
    await page.locator('input[type="password"]').fill(testUser.password);

    // Submit and wait for redirect to device creation page (auto-house flow)
    await Promise.all([
      page.waitForURL(/\/fr\/houses\/[a-f0-9-]+\/devices\/new/, { timeout: 15000 }),
      page.getByRole('button', { name: /s'inscrire|sign up/i }).click()
    ]);

    // Extract house ID from current URL and navigate to house page directly
    const currentUrl = page.url();
    const houseIdMatch = currentUrl.match(/\/houses\/([a-f0-9-]+)\//);
    if (houseIdMatch) {
      await page.goto(`${FRONTEND_URL}/fr/houses/${houseIdMatch[1]}`);
      await page.waitForLoadState('networkidle');
    }

    await use(page);
  },
});

export { expect };
