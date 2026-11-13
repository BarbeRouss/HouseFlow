import { test as base, expect, APIRequestContext } from '@playwright/test';
import { addRefreshCookie, createHouseViaApi, createInvitationViaApi, registerViaApi } from '../fixtures/auth';

const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';

function uniqueEmail(): string {
  return `test-${Date.now()}-${Math.random().toString(36).substring(7)}@houseflow.test`;
}

type Session = { token: string; refreshCookie: string };

async function registerOwner(request: APIRequestContext, firstName: string): Promise<Session> {
  const user = await registerViaApi(request, { firstName, lastName: 'Proprio', email: uniqueEmail() });
  return { token: user.token, refreshCookie: user.refreshCookie };
}

/** Invite a new user who registers with the invitation token (accepted at once, P03). */
async function inviteAndAccept(
  request: APIRequestContext,
  ownerToken: string,
  houseId: string,
  role: 'CollaboratorRW' | 'CollaboratorRO' | 'Tenant',
  firstName: string,
): Promise<Session> {
  const invitation = await createInvitationViaApi(request, ownerToken, houseId, role, uniqueEmail());
  const member = await registerViaApi(
    request,
    { firstName, lastName: 'Invité', email: invitation.email },
    invitation.token,
  );
  expect(member.joinedHouseId).toBe(houseId);
  return { token: member.token, refreshCookie: member.refreshCookie };
}

// Own the auth (API-registered users, refresh cookie): no fixture.
const test = base;

test.describe('P14 · Collaborateurs', () => {
  test('Un propriétaire ouvre la page depuis P08 et voit ses membres groupés par maison', async ({ page, request }) => {
    const owner = await registerOwner(request, 'Odile');
    const houseA = await createHouseViaApi(request, owner.token, 'Maison Alpha');
    const houseB = await createHouseViaApi(request, owner.token, 'Maison Bêta');
    await inviteAndAccept(request, owner.token, houseA, 'CollaboratorRW', 'Roxane');
    await inviteAndAccept(request, owner.token, houseB, 'Tenant', 'Thibault');
    const pending = await createInvitationViaApi(request, owner.token, houseA, 'CollaboratorRO', uniqueEmail());

    await addRefreshCookie(page.context(), owner.refreshCookie);
    await page.goto(`${FRONTEND_URL}/fr/houses`);

    // The entry point is a secondary link on P08; the main navigation keeps its two entries.
    const link = page.getByTestId('collaborators-link');
    await expect(link).toBeVisible();
    await link.click();
    await expect(page).toHaveURL(/\/fr\/collaborators$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Collaborateurs' })).toBeVisible();

    const blocks = page.getByTestId('collaborators-house');
    await expect(blocks).toHaveCount(2);

    const alpha = page.locator(`[data-testid="collaborators-house"][data-house-id="${houseA}"]`);
    await expect(alpha.getByTestId('collaborators-house-title')).toHaveText('Maison Alpha');
    const member = alpha.getByTestId('collaborator-row');
    await expect(member).toHaveCount(1);
    await expect(member).toContainText('Roxane Invité');
    await expect(member.getByTestId('collaborator-role')).toHaveText('Collaborateur');
    await expect(member.getByTestId('collaborator-added')).toContainText(`Ajouté le`);
    await expect(member.getByTestId('collaborator-added')).toContainText(String(new Date().getFullYear()));

    // Pending invitation: e-mail + « Invitation en attente », the link / token is never shown.
    const invitation = alpha.getByTestId('collaborator-invitation');
    await expect(invitation).toHaveCount(1);
    await expect(invitation).toContainText(pending.email);
    await expect(invitation).toContainText('Invitation en attente');
    await expect(page.locator('body')).not.toContainText(pending.token);
    expect(await page.content()).not.toContain(pending.token);

    const beta = page.locator(`[data-testid="collaborators-house"][data-house-id="${houseB}"]`);
    await expect(beta.getByTestId('collaborator-row')).toHaveCount(1);
    await expect(beta.getByTestId('collaborator-role')).toHaveText('Locataire');

    // Read-only: nothing to click to change an access.
    await expect(page.getByTestId('collaborators-page').getByRole('button')).toHaveCount(0);

    // The house title leads to the house (P09), where the access is managed.
    await alpha.getByTestId('collaborators-house-title').click();
    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseA}$`));
  });

  test('Un collaborateur (non propriétaire) n’a pas le lien sur P08 et obtient la page 403', async ({ page, request }) => {
    const owner = await registerOwner(request, 'Olivier');
    const houseId = await createHouseViaApi(request, owner.token, 'Maison partagée');
    const member = await inviteAndAccept(request, owner.token, houseId, 'CollaboratorRW', 'Colette');

    await addRefreshCookie(page.context(), member.refreshCookie);
    await page.goto(`${FRONTEND_URL}/fr/houses`);
    await expect(page.getByTestId('houses-list')).toBeVisible(); // the shared house is listed…
    await expect(page.getByTestId('collaborators-link')).toHaveCount(0); // …but the link is not

    await page.goto(`${FRONTEND_URL}/fr/collaborators`);
    await expect(page.getByTestId('collaborators-forbidden')).toBeVisible();
    await expect(page.getByText(/vous n'avez pas accès à cette page/i)).toBeVisible();
  });

  test('Un propriétaire sans aucun membre voit l’état vide, qui renvoie vers ses maisons', async ({ page, request }) => {
    const owner = await registerOwner(request, 'Ondine');
    await createHouseViaApi(request, owner.token, 'Maison seule');

    await addRefreshCookie(page.context(), owner.refreshCookie);
    await page.goto(`${FRONTEND_URL}/fr/collaborators`);

    await expect(page.getByTestId('collaborators-empty')).toBeVisible();
    await expect(page.getByTestId('collaborators-list')).toHaveCount(0);
    await page.getByTestId('collaborators-empty-action').click();
    await expect(page).toHaveURL(/\/fr\/houses$/);
  });
});
