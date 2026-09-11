import { test as base, expect, Page, APIRequestContext } from '@playwright/test';

const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';
const API_URL = process.env.API_URL || 'http://localhost:5203';

// Bootstrap administrator for E2E runs: injected into the API via the
// Admin__BootstrapEmails__1 env var (scripts/dev-api.sh, .github/workflows/pr.yml).
const ADMIN_EMAIL = 'e2e-admin@houseflow.test';
const PASSWORD = 'TestPassword123!';

function uniqueEmail(): string {
  return `test-${Date.now()}-${Math.random().toString(36).substring(7)}@houseflow.test`;
}

async function registerUser(request: APIRequestContext, firstName: string, lastName: string, email = uniqueEmail()) {
  const res = await request.post(`${API_URL}/api/v1/auth/register`, {
    data: { firstName, lastName, email, password: PASSWORD },
  });
  return res;
}

/**
 * Make sure the bootstrap admin account exists and is flagged admin. Registration may be
 * refused when the account already exists (persisted dev DB, or the other Playwright worker
 * registering it at the very same moment), in which case a login proves the credentials.
 */
async function ensureBootstrapAdmin(request: APIRequestContext) {
  let res = await registerUser(request, 'E2E', 'Admin', ADMIN_EMAIL);
  if (!res.ok()) {
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN_EMAIL, password: PASSWORD } });
  }
  expect(res.ok(), `bootstrap admin unavailable (HTTP ${res.status()})`).toBeTruthy();
  const auth = await res.json();
  expect(auth.user.isAdmin, 'bootstrap e-mail must be flagged admin').toBe(true);
}

async function loginViaUi(page: Page, email: string) {
  await page.goto(`${FRONTEND_URL}/fr/login`);
  await page.getByPlaceholder('you@example.com').fill(email);
  await page.getByPlaceholder('••••••••').fill(PASSWORD);
  await page.getByRole('button', { name: /se connecter|sign in/i }).click();
  await expect(page).toHaveURL(/\/fr\/(dashboard|houses\/[a-f0-9-]+)$/, { timeout: 15000 });
}

const test = base;

test.describe('Admin interface', () => {
  test.beforeAll(async ({ request }) => {
    await ensureBootstrapAdmin(request);
  });

  test('Regular user has no admin link and gets "access denied" on /admin', async ({ page, request }) => {
    const email = uniqueEmail();
    const res = await registerUser(request, 'Simple', 'Membre', email);
    expect(res.ok()).toBeTruthy();
    expect((await res.json()).user.isAdmin).toBe(false);

    await loginViaUi(page, email);

    // Header dropdown: settings yes, administration no.
    await page.locator('header').getByText('SM').click();
    await expect(page.getByRole('link', { name: /paramètres|settings/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /administration/i })).toHaveCount(0);

    await page.goto(`${FRONTEND_URL}/fr/admin`);
    await expect(page.getByTestId('admin-forbidden')).toBeVisible();
    await expect(page.getByText(/accès refusé|access denied/i)).toBeVisible();
  });

  test('Bootstrap admin reaches the admin page from the header and sees stats + users', async ({ page }) => {
    await loginViaUi(page, ADMIN_EMAIL);

    await page.locator('header').getByText('EA').click();
    await page.getByRole('link', { name: /administration/i }).click();
    await expect(page).toHaveURL(/\/fr\/admin$/);

    await expect(page.getByRole('heading', { name: /administration/i })).toBeVisible();
    // 5 KPI tiles with numeric values.
    await expect(page.getByTestId('stat-value')).toHaveCount(5);
    for (const value of await page.getByTestId('stat-value').allTextContents()) {
      expect(value.trim()).toMatch(/^\d+$/);
    }

    // The admin's own row is listed, flagged admin, marked "(vous)" and has no toggle button.
    const selfRow = page.locator('[data-testid="admin-user-row"]', { hasText: ADMIN_EMAIL });
    await expect(selfRow).toBeVisible();
    await expect(selfRow.getByTestId('admin-badge')).toBeVisible();
    await expect(selfRow.getByText(/\(vous\)|\(you\)/)).toBeVisible();
    await expect(selfRow.getByRole('button')).toHaveCount(0);
  });

  test('Admin can search a user, grant admin rights, then revoke them', async ({ page, request }) => {
    const email = uniqueEmail();
    const res = await registerUser(request, 'Promu', 'Candidat', email);
    expect(res.ok()).toBeTruthy();

    await loginViaUi(page, ADMIN_EMAIL);
    await page.goto(`${FRONTEND_URL}/fr/admin`);

    // Search narrows the list to the new user.
    await page.getByPlaceholder(/rechercher par email|search by email/i).fill(email);
    await page.getByRole('button', { name: /^rechercher$|^search$/i }).click();
    const row = page.locator('[data-testid="admin-user-row"]', { hasText: email });
    await expect(row).toBeVisible();
    await expect(page.getByTestId('admin-user-row')).toHaveCount(1);
    await expect(row.getByTestId('user-badge')).toBeVisible();

    // Grant → confirmation modal → badge switches to Admin.
    await row.getByRole('button', { name: /promouvoir admin|make admin/i }).click();
    await expect(page.getByRole('heading', { name: /accorder les droits|grant administrator/i })).toBeVisible();
    await Promise.all([
      page.waitForResponse(r => r.url().includes('/api/v1/admin/users/') && r.request().method() === 'PUT' && r.ok()),
      page.getByRole('button', { name: /^confirmer$|^confirm$/i }).click(),
    ]);
    await expect(row.getByTestId('admin-badge')).toBeVisible();

    // The change is real: the promoted user's next login carries the admin flag.
    const login = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email, password: PASSWORD } });
    expect(login.ok()).toBeTruthy();
    expect((await login.json()).user.isAdmin).toBe(true);

    // Revoke → back to a regular user.
    await row.getByRole('button', { name: /retirer admin|remove admin/i }).click();
    await Promise.all([
      page.waitForResponse(r => r.url().includes('/api/v1/admin/users/') && r.request().method() === 'PUT' && r.ok()),
      page.getByRole('button', { name: /^confirmer$|^confirm$/i }).click(),
    ]);
    await expect(row.getByTestId('admin-badge')).toHaveCount(0);
    await expect(row.getByTestId('user-badge')).toBeVisible();
    await expect(row.getByRole('button', { name: /promouvoir admin|make admin/i })).toBeVisible();

    const relogin = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email, password: PASSWORD } });
    expect((await relogin.json()).user.isAdmin).toBe(false);
  });
});
