import { test, expect, addRefreshCookie, refreshCookieFrom } from '../fixtures/auth';

const API_URL = process.env.API_URL || 'http://localhost:5203';
const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';

// ---------------------------------------------------------------------------
// Demo login button (DEMO_MODE) — the one-click "Connexion démo" button that
// was on the old Next.js login page.
// ---------------------------------------------------------------------------
test.describe('Demo login', () => {
  test('Demo login button signs in as the demo user', async ({ page }) => {
    await page.goto(`${FRONTEND_URL}/fr/login`);
    const demoButton = page.getByRole('button', { name: /connexion démo|demo login/i });
    await expect(demoButton).toBeVisible();
    await demoButton.click();
    // Demo user is authenticated → lands on dashboard (or their single house).
    await expect(page).toHaveURL(/\/fr\/(dashboard|houses\/[a-f0-9-]+)$/, { timeout: 15000 });
  });
});

// ---------------------------------------------------------------------------
// Dashboard "Tâches à venir" (upcoming maintenance tasks) section.
// ---------------------------------------------------------------------------
test.describe('Dashboard upcoming tasks', () => {
  test('Upcoming maintenance task appears on the dashboard', async ({ authenticatedPage: page }) => {
    // Add a device to the auto-created house.
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('VMC Dashboard');
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: 'VMC' }).click();
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // Open it and add a (monthly) maintenance type — never logged ⇒ pending ⇒ upcoming.
    await page.getByRole('heading', { name: 'VMC Dashboard' }).click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);
    await page.getByRole('button', { name: /ajouter|add type/i }).click();
    await expect(page.locator('[class*="fixed"][class*="inset-0"]')).toBeVisible({ timeout: 5000 });
    await page.getByPlaceholder(/révision|annual/i).fill('Nettoyage Tableau');
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: /mensuel/i }).click();
    await Promise.all([
      page.waitForResponse(r => r.url().includes('/maintenance-types') && r.request().method() === 'POST'),
      page.getByRole('button', { name: /ajouter|add$/i }).last().click(),
    ]);
    await expect(page.locator('[class*="fixed"][class*="inset-0"][class*="bg-black"]')).toBeHidden({ timeout: 10000 });

    // Dashboard shows the upcoming-tasks section with the pending task.
    await page.goto(`${FRONTEND_URL}/fr/dashboard`);
    await expect(page.getByRole('heading', { name: /tâches à venir|upcoming tasks/i })).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Nettoyage Tableau')).toBeVisible();
    await expect(page.getByText('VMC Dashboard').first()).toBeVisible();
  });
});

// ---------------------------------------------------------------------------
// Edit device dialog — full fields (brand / model / type), not just the name.
// ---------------------------------------------------------------------------
test.describe('Edit device', () => {
  test('Editing a device updates brand and model', async ({ authenticatedPage: page }) => {
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Appareil Edition');
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: 'Toiture' }).click();
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    await page.getByRole('heading', { name: 'Appareil Edition' }).click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);

    await page.getByRole('button', { name: /modifier l'appareil|edit device/i }).click();
    await expect(page.locator('[class*="fixed"][class*="inset-0"]')).toBeVisible({ timeout: 5000 });
    await page.getByPlaceholder('Viessmann').fill('Bosch');
    await page.getByPlaceholder('Vitodens 200').fill('GC7000');
    await Promise.all([
      page.waitForResponse(r => /\/devices\/[a-f0-9-]+$/.test(r.url()) && r.request().method() === 'PUT'),
      page.getByRole('button', { name: /save|enregistrer/i }).last().click(),
    ]);

    // The header now shows "brand model" together.
    await expect(page.getByText('Bosch GC7000')).toBeVisible({ timeout: 10000 });
  });
});

// ---------------------------------------------------------------------------
// Settings / API keys — scope selection, created-key banner, copy, revoke.
// ---------------------------------------------------------------------------
test.describe('API keys', () => {
  test('Create (with scope), then revoke an API key', async ({ authenticatedPage: page }) => {
    await page.goto(`${FRONTEND_URL}/fr/settings`);

    await page.getByRole('button', { name: /créer une clé api|create api key/i }).first().click();
    await page.getByPlaceholder(/home assistant/i).fill('Clé Test E2E');
    // Pick the read-only scope (radio label).
    await page.getByText(/lecture seule|read.?only/i).click();

    await Promise.all([
      page.waitForResponse(r => r.url().includes('/api-keys') && r.request().method() === 'POST'),
      page.getByRole('button', { name: /créer une clé api|create api key/i }).last().click(),
    ]);

    // Created-key banner + copy feedback.
    await expect(page.getByText(/clé api créée|api key created/i)).toBeVisible({ timeout: 10000 });
    await page.getByRole('button', { name: /copier|^copy$/i }).click();
    await expect(page.getByText(/copié|copied/i)).toBeVisible();

    // Key shows in the list.
    await expect(page.getByText('Clé Test E2E')).toBeVisible();

    // Revoke with the confirmation dialog.
    await page.getByRole('button', { name: /révoquer|revoke/i }).first().click();
    await expect(page.getByText(/révoquer la clé|revoke.*key/i)).toBeVisible({ timeout: 5000 });
    await Promise.all([
      page.waitForResponse(r => r.url().includes('/api-keys') && r.request().method() === 'DELETE'),
      page.locator('[class*="fixed"][class*="inset-0"]').getByRole('button', { name: /révoquer|revoke/i }).click(),
    ]);
    await expect(page.getByText('Clé Test E2E')).toBeHidden({ timeout: 10000 });
  });
});

// ---------------------------------------------------------------------------
// UI regressions: Lucide icons must render, and the custom select must display
// the selected value in its trigger.
// ---------------------------------------------------------------------------
test.describe('UI rendering', () => {
  test('Lucide icons render (non-empty svg)', async ({ authenticatedPage: page }) => {
    const withChildren = await page.$$eval('svg.lucide-icon', els => els.filter(e => e.children.length > 0).length);
    expect(withChildren).toBeGreaterThan(0);
  });

  test('Select trigger shows the chosen value', async ({ authenticatedPage: page }) => {
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.waitForURL(/\/devices\/new/);
    const combo = page.getByRole('combobox');
    await combo.click();
    await page.getByRole('option', { name: 'Toiture' }).click();
    await expect(combo).toContainText('Toiture');
  });
});

// ---------------------------------------------------------------------------
// Tenant permission toggles in the members section (owner view).
// ---------------------------------------------------------------------------
test.describe('Tenant permissions', () => {
  function uniqueEmail(): string {
    return `test-${Date.now()}-${Math.random().toString(36).substring(7)}@houseflow.test`;
  }

  test('Owner sees canLogMaintenance / canViewCosts toggles for a tenant', async ({ page, request }) => {
    // Owner + house.
    const ownerRes = await request.post(`${API_URL}/api/v1/auth/register`, {
      data: { firstName: 'Own', lastName: 'Er', email: uniqueEmail(), password: 'TestPassword123!' },
    });
    const owner = await ownerRes.json();
    const houses = await (await request.get(`${API_URL}/api/v1/houses`, {
      headers: { Authorization: `Bearer ${owner.accessToken}` },
    })).json();
    const houseId = houses.houses[0].id;

    // Invite a Tenant and accept it as a new user.
    const inv = await (await request.post(`${API_URL}/api/v1/houses/${houseId}/invitations`, {
      headers: { Authorization: `Bearer ${owner.accessToken}` },
      data: { role: 'Tenant' },
    })).json();
    const tenant = await (await request.post(`${API_URL}/api/v1/auth/register`, {
      data: { firstName: 'Ten', lastName: 'Ant', email: uniqueEmail(), password: 'TestPassword123!' },
    })).json();
    await request.post(`${API_URL}/api/v1/invitations/${inv.token}/accept`, {
      headers: { Authorization: `Bearer ${tenant.accessToken}` },
    });

    // Log in to the frontend as the owner with the refresh cookie (exchanged at boot).
    await addRefreshCookie(page.context(), refreshCookieFrom(ownerRes));
    await page.goto(`${FRONTEND_URL}/fr/houses/${houseId}`);

    // Open the tenant member's role dropdown → permission checkboxes are shown.
    await page.getByRole('button', { name: /changer le rôle|change role/i }).first().click();
    await expect(page.getByText(/peut enregistrer des entretiens|can log maintenance/i)).toBeVisible({ timeout: 10000 });
    await expect(page.getByText(/peut voir les coûts|can view costs/i)).toBeVisible();
  });
});
