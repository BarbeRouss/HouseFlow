import { test, expect, addRefreshCookie, refreshCookieFrom, createHouseViaApi, createInvitationViaApi } from '../fixtures/auth';
import { HousePage } from '../pages/house-page';

const API_URL = process.env.API_URL || `http://localhost:${process.env.API_PORT || 5203}`;
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
    // Add a device to the house (M2 → lands on its page, P10).
    await new HousePage(page).addDevice({ type: 'other', name: 'VMC Dashboard' });

    // Add a maintenance (M4) — « Je ne sais pas » ⇒ due in 30 days, inside the annual 10 % window ⇒ to handle.
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);
    await page.getByTestId('add-maintenance-type').click();
    const modal = page.getByTestId('type-modal');
    await modal.getByTestId('type-name').fill('Nettoyage Tableau');
    await modal.getByTestId('type-freq-12').click();
    await Promise.all([
      page.waitForResponse(r => r.url().includes('/maintenance-types') && r.request().method() === 'POST'),
      modal.getByTestId('type-save').click(),
    ]);
    await expect(modal).toBeHidden({ timeout: 10000 });

    // P07 lists it in « À faire prochainement ».
    await page.goto(`${FRONTEND_URL}/fr/dashboard`);
    const row = page.getByTestId('group-due').getByTestId('maintenance-row').filter({ hasText: 'Nettoyage Tableau' });
    await expect(row).toBeVisible({ timeout: 10000 });
    await expect(row.getByTestId('maintenance-row-subtitle')).toContainText('VMC Dashboard');
  });
});

// ---------------------------------------------------------------------------
// Edit device dialog — full fields (brand / model / type), not just the name.
// ---------------------------------------------------------------------------
test.describe('Edit device', () => {
  test('Editing a device updates brand and model', async ({ authenticatedPage: page }) => {
    // M2 → lands on the new device's page (P10).
    await new HousePage(page).addDevice({ type: 'other', name: 'Appareil Edition' });
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);

    // P10 header ⋯ → « Modifier l'appareil » (M2 in edit mode).
    await page.getByTestId('device-menu').click();
    await page.getByTestId('device-edit').click();
    await expect(page.getByTestId('device-modal')).toBeVisible({ timeout: 5000 });
    await page.getByTestId('device-brand').fill('Bosch');
    await page.getByTestId('device-model').fill('GC7000');
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

    await page.getByRole('button', { name: /nouvelle clé|new key/i }).click();
    await page.getByPlaceholder(/home assistant/i).fill('Clé Test E2E');
    // Pick the read-only scope (radio label).
    await page.getByText(/lecture seule|read.?only/i).click();

    await Promise.all([
      page.waitForResponse(r => r.url().includes('/api-keys') && r.request().method() === 'POST'),
      page.getByRole('button', { name: /créer une clé api|create api key/i }).click(),
    ]);

    // Created-key banner + copy feedback.
    await expect(page.getByText(/clé api créée|api key created/i)).toBeVisible({ timeout: 10000 });
    await page.getByRole('button', { name: /copier|^copy$/i }).click();
    await expect(page.getByText(/copié|copied/i)).toBeVisible();

    // Key shows in the list (scoped: the M6 title « Révoquer Clé Test E2E ? » also contains the name).
    const keyList = page.getByTestId('api-keys');
    await expect(keyList.getByText('Clé Test E2E')).toBeVisible();

    // Revoke with the confirmation dialog.
    await page.getByRole('button', { name: /révoquer|revoke/i }).first().click();
    // M6: "Révoquer {nom} ?"
    await expect(page.getByTestId('confirm-dialog')).toContainText(/révoquer clé test e2e|revoke clé test e2e/i, { timeout: 5000 });
    await Promise.all([
      page.waitForResponse(r => r.url().includes('/api-keys') && r.request().method() === 'DELETE'),
      page.getByTestId('confirm-dialog').getByTestId('confirm-action').click(),
    ]);
    await expect(page.getByTestId('confirm-dialog')).toBeHidden({ timeout: 10000 });
    await expect(keyList.getByText('Clé Test E2E')).toBeHidden({ timeout: 10000 });
  });
});

// ---------------------------------------------------------------------------
// UI regressions: Lucide icons must render, and the custom select must display
// the selected value in its trigger.
// ---------------------------------------------------------------------------
test.describe('UI rendering', () => {
  test('Lucide icons render (non-empty svg)', async ({ authenticatedPage: page }) => {
    // The Lucide set loads asynchronously after the first render: poll instead of sampling once.
    await expect
      .poll(() => page.$$eval('svg.lucide-icon', els => els.filter(e => e.children.length > 0).length))
      .toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// Members (M5, owner only): invite by email + role → link to copy (no email is sent), renew the
// link (« Renvoyer »), cancel the invitation, change a member's role.
// ---------------------------------------------------------------------------
test.describe('Members and invitations (M5)', () => {
  function uniqueEmail(): string {
    return `test-${Date.now()}-${Math.random().toString(36).substring(7)}@houseflow.test`;
  }

  test('Owner invites, renews and cancels an invitation, and changes a role', async ({ page, request }) => {
    // Owner + house.
    const ownerRes = await request.post(`${API_URL}/api/v1/auth/register`, {
      data: { firstName: 'Own', lastName: 'Er', email: uniqueEmail(), password: 'TestPassword123!', consentAccepted: true },
    });
    const owner = await ownerRes.json();
    // Registration creates no house (P05 does): create it through the API.
    const houseId = await createHouseViaApi(request, owner.accessToken, 'Maison Partagée');

    // A tenant who already joined (invitation accepted through the API).
    const tenantEmail = uniqueEmail();
    const inv = await createInvitationViaApi(request, owner.accessToken, houseId, 'Tenant', tenantEmail);
    const tenant = await (await request.post(`${API_URL}/api/v1/auth/register`, {
      data: { firstName: 'Ten', lastName: 'Ant', email: tenantEmail, password: 'TestPassword123!', consentAccepted: true },
    })).json();
    const accepted = await request.post(`${API_URL}/api/v1/invitations/${inv.token}/accept`, {
      headers: { Authorization: `Bearer ${tenant.accessToken}` },
    });
    expect(accepted.ok()).toBeTruthy();

    // Log in to the frontend as the owner with the refresh cookie (exchanged at boot).
    await addRefreshCookie(page.context(), refreshCookieFrom(ownerRes));
    await page.goto(`${FRONTEND_URL}/fr/houses/${houseId}`);

    // Avatars → M5.
    await page.getByTestId('members-avatars').click({ timeout: 10000 });
    const modal = page.getByTestId('members-modal');
    await expect(modal.getByRole('heading', { name: 'Membres' })).toBeVisible();
    await expect(modal.getByTestId('modal-subtitle')).toHaveText('Maison Partagée');

    // Invite: disabled until the email is valid; the role description follows the role.
    const submit = modal.getByTestId('invite-submit');
    await modal.getByTestId('invite-email').fill('pas-un-email');
    await expect(submit).toBeDisabled();
    const inviteeEmail = uniqueEmail();
    await modal.getByTestId('invite-email').fill(inviteeEmail);
    await modal.getByTestId('invite-role').selectOption('CollaboratorRO');
    await expect(modal.getByTestId('invite-role-description')).toHaveText(/consulter la maison et son historique/i);
    await submit.click();

    // The link to copy is shown, the invitation appears as pending.
    const link = modal.getByTestId('invitation-link');
    await expect(link).toHaveValue(/\/fr\/invitations\/[A-Za-z0-9_-]+$/);
    const firstLink = await link.inputValue();
    const row = modal.getByTestId('invitation-row').filter({ hasText: inviteeEmail });
    await expect(row).toContainText(/invitation en attente/i);
    await expect(page.getByTestId('toast')).toHaveText(`Invitation créée pour ${inviteeEmail}`);

    // « Renvoyer » creates a new link (the previous one stops working).
    await row.getByTestId('invitation-resend').click();
    await expect(link).not.toHaveValue(firstLink);
    await expect(link).toHaveValue(/\/fr\/invitations\//);

    // « Annuler l'invitation » (row menu) removes it.
    await row.getByTestId('invitation-menu').click();
    await page.getByTestId('invitation-cancel').click();
    await expect(modal.getByTestId('invitation-row').filter({ hasText: inviteeEmail })).toHaveCount(0);

    // Role change is saved at once.
    const tenantRow = modal.getByTestId('member-row').filter({ hasText: 'Ten Ant' });
    await Promise.all([
      page.waitForResponse(r => /\/members\/[a-f0-9-]+\/role$/.test(r.url()) && r.request().method() === 'PUT' && r.ok()),
      tenantRow.getByTestId('member-role').selectOption('CollaboratorRW'),
    ]);
    const members = await (await request.get(`${API_URL}/api/v1/houses/${houseId}/members`, {
      headers: { Authorization: `Bearer ${owner.accessToken}` },
    })).json();
    expect(members.find((m: { email: string }) => m.email === tenantEmail).role).toBe('CollaboratorRW');

    // ✕ → M6 « Retirer Ten Ant ? » → removed.
    await tenantRow.getByTestId('member-menu').click();
    await page.getByTestId('member-remove').click();
    const confirm = page.getByTestId('remove-member-dialog');
    await expect(confirm.getByRole('heading', { name: 'Retirer Ten Ant ?' })).toBeVisible();
    await confirm.getByTestId('confirm-action').click();
    await expect(modal.getByTestId('member-row')).toHaveCount(1);
  });
});
