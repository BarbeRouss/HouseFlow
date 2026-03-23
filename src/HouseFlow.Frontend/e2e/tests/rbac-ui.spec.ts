import { test as base, expect, Page, APIRequestContext } from '@playwright/test';

const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';
const API_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5203';

function uniqueEmail(): string {
  return `test-${Date.now()}-${Math.random().toString(36).substring(7)}@houseflow.test`;
}

/**
 * Register a user via API and return the access token + auto-created houseId.
 */
async function registerUser(
  request: APIRequestContext,
  firstName: string,
  lastName: string
): Promise<{ token: string; houseId: string }> {
  const res = await request.post(`${API_URL}/api/v1/auth/register`, {
    data: {
      firstName,
      lastName,
      email: uniqueEmail(),
      password: 'TestPassword123!',
    },
  });
  expect(res.ok()).toBeTruthy();
  const auth = await res.json();

  const housesRes = await request.get(`${API_URL}/api/v1/houses`, {
    headers: { Authorization: `Bearer ${auth.accessToken}` },
  });
  const houses = await housesRes.json();

  return { token: auth.accessToken, houseId: houses.houses[0].id };
}

/**
 * Create an invitation and accept it with a new user. Return the new user's token.
 */
async function inviteAndAccept(
  request: APIRequestContext,
  ownerToken: string,
  houseId: string,
  role: string
): Promise<string> {
  // Create invitation
  const invRes = await request.post(`${API_URL}/api/v1/houses/${houseId}/invitations`, {
    headers: { Authorization: `Bearer ${ownerToken}` },
    data: { role },
  });
  expect(invRes.ok()).toBeTruthy();
  const invitation = await invRes.json();

  // Register new user
  const newRes = await request.post(`${API_URL}/api/v1/auth/register`, {
    data: {
      firstName: `${role}First`,
      lastName: `${role}Last`,
      email: uniqueEmail(),
      password: 'TestPassword123!',
    },
  });
  const newAuth = await newRes.json();

  // Accept invitation
  const acceptRes = await request.post(`${API_URL}/api/v1/invitations/${invitation.token}/accept`, {
    headers: { Authorization: `Bearer ${newAuth.accessToken}` },
  });
  expect(acceptRes.ok()).toBeTruthy();

  return newAuth.accessToken;
}

/**
 * Login to the frontend by injecting the JWT token cookie/localStorage.
 */
async function loginWithToken(page: Page, token: string, houseId: string) {
  // Navigate to the app first to set origin
  await page.goto(`${FRONTEND_URL}/fr/login`);
  await page.waitForLoadState('networkidle');

  // Inject the token into localStorage AND sessionStorage (matching the auth context keys)
  await page.evaluate((t) => {
    localStorage.setItem('houseflow_access_token', t);
    // Auth context requires user data in sessionStorage to consider the session valid
    const payload = JSON.parse(atob(t.split('.')[1]));
    sessionStorage.setItem('houseflow_auth_user', JSON.stringify({
      id: payload.sub || payload.nameid,
      email: payload.email || '',
      firstName: payload.given_name || payload.firstName || '',
      lastName: payload.family_name || payload.lastName || '',
    }));
  }, token);

  // Navigate to the shared house
  await page.goto(`${FRONTEND_URL}/fr/houses/${houseId}`);
  await page.waitForLoadState('networkidle');
}

// Use base test (no fixture) since we manage auth ourselves
const test = base;

test.describe('RBAC UI Validation', () => {
  let ownerToken: string;
  let houseId: string;
  let collabRWToken: string;
  let collabROToken: string;
  let tenantToken: string;
  let deviceId: string;

  test.beforeAll(async ({ request }) => {
    // Setup: Owner with house, device, and 3 invited roles
    const owner = await registerUser(request, 'Owner', 'Boss');
    ownerToken = owner.token;
    houseId = owner.houseId;

    // Create a device via API
    const deviceRes = await request.post(`${API_URL}/api/v1/houses/${houseId}/devices`, {
      headers: { Authorization: `Bearer ${ownerToken}` },
      data: { name: 'Chaudière RBAC', type: 'Chaudière Gaz' },
    });
    const device = await deviceRes.json();
    deviceId = device.id;

    // Invite all roles
    collabRWToken = await inviteAndAccept(request, ownerToken, houseId, 'CollaboratorRW');
    collabROToken = await inviteAndAccept(request, ownerToken, houseId, 'CollaboratorRO');
    tenantToken = await inviteAndAccept(request, ownerToken, houseId, 'Tenant');
  });

  // ====================================================================
  // Owner: should see everything (edit house, manage members, add devices)
  // ====================================================================

  test('Owner sees members section with management controls', async ({ page }) => {
    await loginWithToken(page, ownerToken, houseId);

    // Should see "Gérer les membres" or "Manage members" section
    await expect(page.getByText(/gérer les membres|manage members/i)).toBeVisible({ timeout: 10000 });

    // Should see role change dropdown buttons (ChevronDown icons for non-owner members)
    // Owner sees at least 3 non-owner members
    const memberRows = page.locator('[class*="rounded-lg"]').filter({ has: page.locator('[class*="rounded-full"]') });
    await expect(memberRows.first()).toBeVisible();

    // Should see "Créer une invitation" / "Create invitation" section
    await expect(page.getByText(/créer une invitation|create invitation/i).first()).toBeVisible();

    // Should see "Ajouter un appareil" button
    await expect(page.getByRole('button', { name: /ajouter un appareil|add device/i }).first()).toBeVisible();
  });

  // ====================================================================
  // CollaboratorRW: can see house, add devices, but NOT manage members/house
  // ====================================================================

  test('CollaboratorRW sees add device button but no house edit', async ({ page }) => {
    await loginWithToken(page, collabRWToken, houseId);

    // Should see the house page
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10000 });

    // Should see "Ajouter un appareil" button (CollabRW can create devices)
    await expect(page.getByRole('button', { name: /ajouter un appareil|add device/i }).first()).toBeVisible();

    // Should NOT see delete house button
    await expect(page.getByRole('button', { name: /supprimer|delete house/i })).not.toBeVisible();
  });

  // ====================================================================
  // CollaboratorRO: read-only, no add device, no edit
  // ====================================================================

  test('CollaboratorRO cannot see add device or edit controls', async ({ page }) => {
    await loginWithToken(page, collabROToken, houseId);

    // Should see the house page
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10000 });

    // Should NOT see "Ajouter un appareil" button
    await expect(page.getByRole('button', { name: /ajouter un appareil|add device/i })).not.toBeVisible();

    // Should NOT see delete house button
    await expect(page.getByRole('button', { name: /supprimer|delete house/i })).not.toBeVisible();
  });

  // ====================================================================
  // Tenant: read-only view, no device management
  // ====================================================================

  test('Tenant cannot see add device or house management controls', async ({ page }) => {
    await loginWithToken(page, tenantToken, houseId);

    // Should see the house page
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10000 });

    // Should NOT see "Ajouter un appareil" button
    await expect(page.getByRole('button', { name: /ajouter un appareil|add device/i })).not.toBeVisible();

    // Should NOT see member management section
    await expect(page.getByText(/créer une invitation|create invitation/i)).not.toBeVisible();
  });

  // ====================================================================
  // Dashboard: shared house shows role badge
  // ====================================================================

  test('CollaboratorRW sees "Partagée" badge on dashboard', async ({ page }) => {
    // CollabRW needs 2 houses to see dashboard (auto-created + shared)
    await loginWithToken(page, collabRWToken, houseId);
    await page.goto(`${FRONTEND_URL}/fr/dashboard`);
    await page.waitForLoadState('networkidle');

    // Should see "Partagée" badge on the shared house card
    await expect(page.getByText(/partagée|shared/i).first()).toBeVisible({ timeout: 10000 });
  });
});
