import { test as base, expect, Page, APIResponse, APIRequestContext, BrowserContext } from '@playwright/test';

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

type TestUser = {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  /** RGPD — acceptation des CGU : le backend refuse l'inscription sans elle (400). */
  consentAccepted: boolean;
};

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
      consentAccepted: true,
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

    // RGPD — l'acceptation des CGU est obligatoire : le bouton reste désactivé sans elle.
    await page.locator('#acceptTerms').check();

    // P03 → P05: registration no longer creates a house; the setup step does.
    await Promise.all([
      page.waitForURL(/\/fr\/setup\/house$/, { timeout: 15000 }),
      page.getByTestId('register-submit').click(),
    ]);

    // P05: keep the prefilled name « Ma maison » and create it → P06 (?house={id}).
    await expect(page.getByTestId('setup-house-name')).toHaveValue('Ma maison');
    await Promise.all([
      page.waitForURL(/\/fr\/setup\/devices\?house=[a-f0-9-]+/, { timeout: 15000 }),
      page.getByTestId('setup-house-continue').click(),
    ]);

    // Skip the equipment step: tests start from the (empty) house page.
    const houseId = new URL(page.url()).searchParams.get('house');
    if (!houseId) throw new Error(`No house id in ${page.url()}`);
    await page.goto(`${FRONTEND_URL}/fr/houses/${houseId}`);
    await page.waitForLoadState('networkidle');

    await use(page);
  },
});

/**
 * Register a user through the API (use an isolated `request` context, not `page.request`, or the
 * refresh cookie logs the page in). Registration creates no house: use {@link createHouseViaApi}.
 */
export async function registerViaApi(
  request: APIRequestContext,
  user: { firstName: string; lastName: string; email?: string; password?: string },
  invitationToken?: string,
): Promise<{ token: string; refreshCookie: string; email: string; userId: string; joinedHouseId?: string }> {
  const email = user.email ?? generateTestEmail();
  const url = invitationToken
    ? `${API_URL}/api/v1/auth/register?invitationToken=${encodeURIComponent(invitationToken)}`
    : `${API_URL}/api/v1/auth/register`;
  const res = await request.post(url, {
    data: {
      firstName: user.firstName,
      lastName: user.lastName,
      email,
      password: user.password ?? 'TestPassword123!',
      // RGPD — acceptation des CGU obligatoire (sinon 400).
      consentAccepted: true,
    },
  });
  expect(res.ok(), `register ${email}: HTTP ${res.status()}`).toBeTruthy();
  const auth = await res.json();
  return {
    token: auth.accessToken,
    refreshCookie: refreshCookieFrom(res),
    email,
    userId: auth.user?.id,
    joinedHouseId: auth.joinedHouseId ?? undefined,
  };
}

/** Create a house owned by the token's user (what P05 does) and return its id. */
export async function createHouseViaApi(request: APIRequestContext, token: string, name = 'Ma maison'): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/houses`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { name },
  });
  expect(res.ok(), `create house: HTTP ${res.status()}`).toBeTruthy();
  return (await res.json()).id;
}

/** Owner-side invitation (M5): invitee email + role. No email is sent; the link carries the token. */
export async function createInvitationViaApi(
  request: APIRequestContext,
  ownerToken: string,
  houseId: string,
  role: 'CollaboratorRW' | 'CollaboratorRO' | 'Tenant',
  email: string = generateTestEmail(),
): Promise<{ token: string; email: string; id: string }> {
  const res = await request.post(`${API_URL}/api/v1/houses/${houseId}/invitations`, {
    headers: { Authorization: `Bearer ${ownerToken}` },
    data: { email, role },
  });
  expect(res.ok(), `create invitation: HTTP ${res.status()}`).toBeTruthy();
  const inv = await res.json();
  return { token: inv.token, email, id: inv.id };
}

export { expect };
