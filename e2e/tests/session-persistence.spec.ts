import { test, expect, Page, APIRequestContext } from '@playwright/test';
import { generateTestEmail, SESSION_HINT_KEY } from '../fixtures/auth';

const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';
const API_URL = process.env.API_URL || 'http://localhost:5203';
const PASSWORD = 'TestPassword123!';
const LOGGED_IN_URL = /\/fr\/(dashboard|houses\/[a-f0-9-]+)$/;

// ---------------------------------------------------------------------------
// Session persistence (#164): the access token only lives in memory, the session
// survives through the HttpOnly refresh cookie — persistent for a year with
// "remember me", a browser-session cookie otherwise.
// ---------------------------------------------------------------------------

async function registerViaApi(request: APIRequestContext): Promise<string> {
  const email = generateTestEmail();
  const res = await request.post(`${API_URL}/api/v1/auth/register`, {
    data: { firstName: 'Test', lastName: 'User', email, password: PASSWORD },
  });
  expect(res.ok()).toBeTruthy();
  return email;
}

async function loginViaUi(page: Page, email: string, rememberMe: boolean) {
  await page.goto(`${FRONTEND_URL}/fr/login`);
  const emailField = page.getByPlaceholder('you@example.com');
  await emailField.click();
  await emailField.pressSequentially(email, { delay: 30 });
  await expect(emailField).toHaveValue(email);
  const passwordField = page.getByPlaceholder('••••••••');
  await passwordField.click();
  await passwordField.pressSequentially(PASSWORD, { delay: 30 });
  await expect(passwordField).toHaveValue(PASSWORD);
  if (rememberMe) await page.getByLabel(/se souvenir de moi|remember me/i).check();
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

  test('Logged-out visitor gets the login page without waiting on the API', async ({ page }) => {
    // Regression guard (#164): the first paint must never depend on a network round-trip —
    // the API of an ephemeral environment cold-starts in ~30 s.
    const refreshCalls: string[] = [];
    page.on('request', (r) => { if (r.url().includes('/api/v1/auth/refresh')) refreshCalls.push(r.method()); });

    await page.goto(`${FRONTEND_URL}/fr/login`);
    await expect(page.getByPlaceholder('you@example.com')).toBeVisible({ timeout: 15000 });

    expect(refreshCalls).toEqual([]);
    expect(await page.evaluate((k) => localStorage.getItem(k), SESSION_HINT_KEY)).toBeNull();
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
