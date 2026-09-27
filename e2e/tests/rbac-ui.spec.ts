import { test as base, expect, Page, APIRequestContext } from '@playwright/test';
import { addRefreshCookie, createHouseViaApi, createInvitationViaApi, registerViaApi } from '../fixtures/auth';

const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';
const API_URL = process.env.API_URL || `http://localhost:${process.env.API_PORT || 5203}`;

function uniqueEmail(): string {
  return `test-${Date.now()}-${Math.random().toString(36).substring(7)}@houseflow.test`;
}

type Session = { token: string; refreshCookie: string };

/**
 * Register a user via API and return the access token, the refresh cookie
 * (to log the browser in) and the id of a house created for them (registration
 * creates no house any more: P05 does, here the API).
 */
async function registerUser(
  request: APIRequestContext,
  firstName: string,
  lastName: string
): Promise<Session & { houseId: string }> {
  const user = await registerViaApi(request, { firstName, lastName, email: uniqueEmail() });
  const houseId = await createHouseViaApi(request, user.token);
  return { token: user.token, refreshCookie: user.refreshCookie, houseId };
}

/**
 * Invite a new user (invitee email + role) who registers with the invitation token:
 * the API accepts it automatically (P03 invitation mode). Return the new user's session.
 */
async function inviteAndAccept(
  request: APIRequestContext,
  ownerToken: string,
  houseId: string,
  role: 'CollaboratorRW' | 'CollaboratorRO' | 'Tenant'
): Promise<Session> {
  const invitation = await createInvitationViaApi(request, ownerToken, houseId, role, uniqueEmail());
  const member = await registerViaApi(
    request,
    { firstName: `${role}First`, lastName: `${role}Last`, email: invitation.email },
    invitation.token,
  );
  expect(member.joinedHouseId).toBe(houseId);
  return { token: member.token, refreshCookie: member.refreshCookie };
}

/**
 * Login to the frontend with the user's refresh cookie: the app exchanges it
 * for an access token at boot (the token itself is never stored in the browser).
 */
async function loginWithSession(page: Page, session: Session, houseId: string) {
  await addRefreshCookie(page.context(), session.refreshCookie);

  // Navigate to the shared house
  await page.goto(`${FRONTEND_URL}/fr/houses/${houseId}`);
  await page.waitForLoadState('networkidle');
}

// Use base test (no fixture) since we manage auth ourselves
const test = base;

test.describe('RBAC UI Validation', () => {
  let owner: Session;
  let ownerToken: string;
  let houseId: string;
  let collabRW: Session;
  let collabRO: Session;
  let tenant: Session;
  let deviceId: string;

  test.beforeAll(async ({ request }) => {
    // Setup: Owner with house, device, and 3 invited roles
    owner = await registerUser(request, 'Owner', 'Boss');
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
    collabRW = await inviteAndAccept(request, ownerToken, houseId, 'CollaboratorRW');
    collabRO = await inviteAndAccept(request, ownerToken, houseId, 'CollaboratorRO');
    tenant = await inviteAndAccept(request, ownerToken, houseId, 'Tenant');
  });

  // ====================================================================
  // Owner: ⋯ menu (edit / members / delete), members modal (M5), add devices
  // ====================================================================

  test('Owner sees the house menu, the members modal and « Ajouter un appareil »', async ({ page }) => {
    await loginWithSession(page, owner, houseId);

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('add-device')).toBeVisible();
    await expect(page.getByTestId('members-avatars')).toBeVisible();

    await page.getByTestId('house-menu').click();
    await expect(page.getByTestId('house-menu-edit')).toBeVisible();
    await expect(page.getByTestId('house-menu-delete')).toBeVisible();
    await page.getByTestId('house-menu-members').click();

    // M5: owner first (no action) + the 3 invited members with a role selector and ✕.
    const modal = page.getByTestId('members-modal');
    await expect(modal).toBeVisible();
    const rows = modal.getByTestId('member-row');
    await expect(rows).toHaveCount(4);
    await expect(rows.first()).toHaveAttribute('data-role', 'Owner');
    await expect(rows.first()).toContainText(/\(vous\)/);
    await expect(rows.first().getByTestId('member-role')).toHaveCount(0);
    await expect(modal.getByTestId('member-role')).toHaveCount(3);
    await expect(modal.getByTestId('member-remove')).toHaveCount(3);

    // Invite form: email + role (Collaborateur by default) + its description.
    await expect(modal.getByTestId('invite-email')).toBeVisible();
    await expect(modal.getByTestId('invite-role')).toHaveValue('CollaboratorRW');
    await expect(modal.getByTestId('invite-role-description')).toHaveText(/ajouter des appareils/i);
  });

  // ====================================================================
  // CollaboratorRW: can add devices, but no ⋯ menu and no members management
  // ====================================================================

  test('CollaboratorRW sees « Ajouter un appareil » but no house menu', async ({ page }) => {
    await loginWithSession(page, collabRW, houseId);

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('add-device')).toBeVisible();
    await expect(page.getByTestId('house-shared')).toHaveText(/partagée · collaborateur/i);

    // R5: hidden, never disabled.
    await expect(page.getByTestId('house-menu')).toHaveCount(0);
    await expect(page.getByTestId('members-avatars')).toHaveCount(0);
    await expect(page.getByTestId('avatar-stack')).toBeVisible();
  });

  // ====================================================================
  // CollaboratorRO: read-only, no add device, no menu
  // ====================================================================

  test('CollaboratorRO cannot see add device or edit controls', async ({ page }) => {
    await loginWithSession(page, collabRO, houseId);

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('device-row')).toHaveCount(1);
    await expect(page.getByTestId('add-device')).toHaveCount(0);
    await expect(page.getByTestId('house-menu')).toHaveCount(0);
  });

  // ====================================================================
  // Tenant: read-only view, no device management
  // ====================================================================

  test('Tenant cannot see add device or house management controls', async ({ page }) => {
    await loginWithSession(page, tenant, houseId);

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('add-device')).toHaveCount(0);
    await expect(page.getByTestId('house-menu')).toHaveCount(0);
    await expect(page.getByTestId('members-avatars')).toHaveCount(0);
  });

  // ====================================================================
  // P08: a shared house shows « Partagée · {rôle} »
  // ====================================================================

  test('CollaboratorRW sees the shared house as « Partagée · Collaborateur » on P08', async ({ page }) => {
    await loginWithSession(page, collabRW, houseId);
    await page.goto(`${FRONTEND_URL}/fr/houses`);

    const row = page.locator(`[data-testid="house-row"][data-house-id="${houseId}"]`);
    await expect(row.getByTestId('house-row-subtitle')).toHaveText(/^Partagée · Collaborateur · 1 appareil$/, { timeout: 10000 });
  });
});
