import { test, expect, Page, APIRequestContext, request as playwrightRequest } from '@playwright/test';
import { generateTestEmail, SESSION_HINT_KEY } from '../fixtures/auth';

const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';
const API_URL = process.env.API_URL || `http://localhost:${process.env.API_PORT || 5203}`;
const PASSWORD = 'TestPassword123!';
const LOGGED_IN_URL = /\/fr\/(dashboard|houses\/[a-f0-9-]+)$/;

// ---------------------------------------------------------------------------
// Session persistence (#164): the access token only lives in memory, the session
// survives through the HttpOnly refresh cookie — persistent for a year with
// "remember me" (ticked by default, #381), a browser-session cookie otherwise.
// ---------------------------------------------------------------------------

async function registerViaApi(request: APIRequestContext): Promise<string> {
  const email = generateTestEmail();
  const res = await request.post(`${API_URL}/api/v1/auth/register`, {
    // RGPD — l'acceptation des CGU est obligatoire côté API (Art. 6(1)(b)).
    data: { firstName: 'Test', lastName: 'User', email, password: PASSWORD, consentAccepted: true },
  });
  expect(res.ok()).toBeTruthy();
  return email;
}

/** `rememberMe: 'default'` leaves the checkbox as the page shows it (ticked, #381). */
async function loginViaUi(page: Page, email: string, rememberMe: boolean | 'default', origin = FRONTEND_URL) {
  await page.goto(`${origin}/fr/login`);
  const emailField = page.getByPlaceholder('you@example.com');
  await emailField.click();
  await emailField.pressSequentially(email, { delay: 30 });
  await expect(emailField).toHaveValue(email);
  const passwordField = page.getByPlaceholder('••••••••');
  await passwordField.click();
  await passwordField.pressSequentially(PASSWORD, { delay: 30 });
  await expect(passwordField).toHaveValue(PASSWORD);
  const box = page.getByLabel(/se souvenir de moi|remember me/i);
  if (rememberMe === true) await box.check();
  else if (rememberMe === false) await box.uncheck();
  await page.getByRole('button', { name: /se connecter|sign in/i }).click();
  await expect(page).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
}

test.describe('Session persistence', () => {
  test('Remember me: session survives a browser restart and no token is stored in the browser', async ({ page, browser, request }) => {
    const email = await registerViaApi(request);
    await loginViaUi(page, email, true);

    // The credential never touches browser storage.
    const stored = await page.evaluate(() => ({
      local: Object.keys(localStorage),
      session: Object.keys(sessionStorage),
    }));
    expect(stored.local).not.toContain('houseflow_access_token');
    expect(stored.session).not.toContain('houseflow_auth_user');

    // Persistent refresh cookie, valid for about a year.
    const cookies = await page.context().cookies();
    const refresh = cookies.find((c) => c.name === 'refreshToken');
    expect(refresh).toBeDefined();
    expect(refresh!.httpOnly).toBeTruthy();
    expect(refresh!.expires).toBeGreaterThan(Date.now() / 1000 + 300 * 24 * 3600);

    // "Close and reopen the browser": a fresh context that only inherits what a real
    // browser keeps across restarts — persistent cookies and localStorage (the session hint).
    const restored = await browser.newContext({ storageState: await page.context().storageState() });
    const page2 = await restored.newPage();
    await page2.goto(`${FRONTEND_URL}/fr/dashboard`);
    await expect(page2).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
    await expect(page2.locator('header').getByText('TU')).toBeVisible();
    await restored.close();
  });

  test('Remember me is ticked by default: a plain login stays signed in across a browser restart', async ({ page, browser, request }) => {
    const email = await registerViaApi(request);
    await page.goto(`${FRONTEND_URL}/fr/login`);
    await expect(page.getByLabel(/se souvenir de moi|remember me/i)).toBeChecked();

    await loginViaUi(page, email, 'default');
    const refresh = (await page.context().cookies()).find((c) => c.name === 'refreshToken');
    expect(refresh!.expires).toBeGreaterThan(Date.now() / 1000 + 300 * 24 * 3600);

    const restored = await browser.newContext({ storageState: await page.context().storageState() });
    const page2 = await restored.newPage();
    await page2.goto(`${FRONTEND_URL}/fr/dashboard`);
    await expect(page2).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
    await restored.close();
  });

  test('Registration opens a year-long session', async ({ page }) => {
    const email = generateTestEmail();
    await page.goto(`${FRONTEND_URL}/fr/register`);
    await page.locator('#firstName').fill('Test');
    await page.locator('#lastName').fill('User');
    await page.locator('#email').fill(email);
    await page.locator('#password').fill(PASSWORD);
    await page.locator('#acceptTerms').check();
    await page.getByTestId('register-submit').click();
    await expect(page).not.toHaveURL(/\/register/, { timeout: 15000 });

    const refresh = (await page.context().cookies()).find((c) => c.name === 'refreshToken');
    expect(refresh).toBeDefined();
    expect(refresh!.expires).toBeGreaterThan(Date.now() / 1000 + 300 * 24 * 3600);
  });

  test('Session hint wiped (localStorage purged) but the cookie is valid: the user is signed in anyway', async ({ page, browser, request }) => {
    // Safari purges localStorage after ~7 days without interaction while the HttpOnly cookie lives
    // on: the app must not take the missing hint for "logged out" (#381).
    const email = await registerViaApi(request);
    await loginViaUi(page, email, 'default');

    const state = await page.context().storageState();
    const restored = await browser.newContext({ storageState: { cookies: state.cookies, origins: [] } });
    const page2 = await restored.newPage();
    await page2.goto(`${FRONTEND_URL}/fr/dashboard`);
    // Protected page first sent to the login page, then back where the user asked to go.
    await expect(page2).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
    await expect(page2.locator('header').getByText('TU')).toBeVisible();

    // Same from the login page itself.
    await page2.evaluate((k) => localStorage.removeItem(k), SESSION_HINT_KEY);
    await page2.goto(`${FRONTEND_URL}/fr/login`);
    await expect(page2).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
    await restored.close();
  });

  test('Without remember me: session cookie only, the session ends with the browser', async ({ page, browser, request }) => {
    const email = await registerViaApi(request);
    await loginViaUi(page, email, false);

    const cookies = await page.context().cookies();
    const refresh = cookies.find((c) => c.name === 'refreshToken');
    expect(refresh).toBeDefined();
    expect(refresh!.expires).toBe(-1); // session cookie: no Expires attribute

    // A restarted browser keeps localStorage but only persistent cookies (none here).
    const state = await page.context().storageState();
    const restored = await browser.newContext({
      storageState: { ...state, cookies: state.cookies.filter((c) => c.expires !== -1) },
    });
    const page2 = await restored.newPage();
    await page2.goto(`${FRONTEND_URL}/fr/dashboard`);
    await expect(page2).toHaveURL(/\/fr\/login/, { timeout: 15000 });
    await restored.close();
  });

  test('Cross-site frontend (like the PR previews) keeps the session across reload and restart', async ({ page, browser, request }) => {
    // The frontend is driven from http://127.0.0.1:3000 while it calls the API on
    // http://localhost:5203: a different site for the browser, exactly like a Static Web
    // App talking to a Container App. The refresh cookie must then be SameSite=None; Secure
    // (Auth:CookieSameSite=None on the API, see scripts/dev-api.sh) or it is never stored.
    const CROSS_SITE_ORIGIN = 'http://127.0.0.1:3000';
    const email = await registerViaApi(request);
    await loginViaUi(page, email, true, CROSS_SITE_ORIGIN);

    const cookies = await page.context().cookies();
    const refresh = cookies.find((c) => c.name === 'refreshToken');
    expect(refresh, 'the browser kept the cross-site refresh cookie').toBeDefined();
    expect(refresh!.domain).toBe('localhost');
    expect(refresh!.sameSite).toBe('None');
    expect(refresh!.secure).toBeTruthy();
    expect(refresh!.httpOnly).toBeTruthy();
    expect(await page.evaluate((k) => localStorage.getItem(k), SESSION_HINT_KEY)).toBe('1');

    // Reload: the app exchanges the cookie cross-site for a new access token.
    await page.reload();
    await expect(page).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
    await expect(page.locator('header').getByText('TU')).toBeVisible();

    // Browser restart: cookies + localStorage carried over, nothing else.
    const restored = await browser.newContext({ storageState: await page.context().storageState() });
    const page2 = await restored.newPage();
    await page2.goto(`${CROSS_SITE_ORIGIN}/fr/dashboard`);
    await expect(page2).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
    await expect(page2.locator('header').getByText('TU')).toBeVisible();
    await restored.close();
  });

  test('Logged-out visitor gets the login page without waiting on the API', async ({ page }) => {
    // Regression guard (#164): the first paint must never depend on a network round-trip —
    // the API of an ephemeral environment cold-starts in ~30 s. Without a session hint the app
    // still probes the cookie once (#381), but in the background: hold that answer back and the
    // login page must be there all the same, with no retry banner.
    const refreshCalls: string[] = [];
    await page.route('**/api/v1/auth/refresh', async (route) => {
      refreshCalls.push(route.request().method());
      await new Promise((resolve) => setTimeout(resolve, 5000));
      await route.continue();
    });

    await page.goto(`${FRONTEND_URL}/fr/login`);
    await expect(page.getByPlaceholder('you@example.com')).toBeVisible({ timeout: 3000 });
    await expect(page.getByLabel(/se souvenir de moi|remember me/i)).toBeChecked();

    await expect.poll(() => refreshCalls.length, { timeout: 10000 }).toBe(1);
    await page.waitForTimeout(6000);
    expect(refreshCalls).toEqual(['POST']); // one quiet probe, never retried
    await expect(page).toHaveURL(/\/fr\/login/);
    expect(await page.evaluate((k) => localStorage.getItem(k), SESSION_HINT_KEY)).toBeNull();
  });

  test('Refresh responses lost to reloads during boot do not end the session', async ({ page, request }) => {
    // Reloading while the boot refresh is in flight drops its response after the server has
    // rotated the token, so the browser presents the old cookie again. Two losses in a row
    // (rapid reloads across pages) used to be taken for a stolen cookie: family revoked,
    // user sent back to the login page. Losses are simulated deterministically by spending
    // the browser's cookie from outside the browser, whose jar is left untouched.
    const email = await registerViaApi(request);
    await loginViaUi(page, email, false);
    const cookie = (await page.context().cookies()).find((c) => c.name === 'refreshToken')!;

    for (let i = 0; i < 2; i++) {
      const outside = await playwrightRequest.newContext();
      const lost = await outside.post(`${API_URL}/api/v1/auth/refresh`, {
        headers: { Cookie: `refreshToken=${cookie.value}` },
      });
      expect(lost.ok()).toBeTruthy();
      await outside.dispose();
    }

    await page.goto(`${FRONTEND_URL}/fr/dashboard`);
    await expect(page.locator('header').getByText('TU')).toBeVisible({ timeout: 15000 });
    await page.reload();
    await expect(page.locator('header').getByText('TU')).toBeVisible({ timeout: 15000 });
    await expect(page).not.toHaveURL(/\/login/);
  });

  test('Reload and second tab keep the session (silent refresh at boot)', async ({ page, request }) => {
    const email = await registerViaApi(request);
    await loginViaUi(page, email, false);

    await page.reload();
    await expect(page).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
    await expect(page.locator('header').getByText('TU')).toBeVisible();

    const tab2 = await page.context().newPage();
    await tab2.goto(`${FRONTEND_URL}/fr/dashboard`);
    await expect(tab2).toHaveURL(LOGGED_IN_URL, { timeout: 15000 });
    await expect(tab2.locator('header').getByText('TU')).toBeVisible();
    await tab2.close();
  });
});
