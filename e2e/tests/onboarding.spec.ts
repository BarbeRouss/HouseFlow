import {
  test,
  expect,
  addRefreshCookie,
  createHouseViaApi,
  createInvitationViaApi,
  generateTestEmail,
  registerViaApi,
} from '../fixtures/auth';
import { RegisterPage } from '../pages/register-page';
import { LoginPage } from '../pages/login-page';
import { SetupPage } from '../pages/setup-page';

test.describe('User Flow 1: Onboarding (First Time Experience)', () => {
  test('Complete onboarding: Register -> Setup house -> One device', async ({ page }) => {
    const registerPage = new RegisterPage(page);
    await registerPage.goto();
    await page.waitForLoadState('networkidle');

    await registerPage.register('Jean', 'Dupont', `newuser-${Date.now()}@houseflow.test`, 'SecurePass123!');
    await registerPage.expectRegisterSuccess();

    const setup = new SetupPage(page);
    await setup.createHouse();

    // One chip, « Je ne sais pas » by default → singular label, one preview line.
    await setup.chip('woodStove').click();
    await expect(setup.createTasks).toHaveText(/Créer mon entretien/);
    await expect(setup.previewRows).toHaveCount(1);
    await setup.createTasks.click();

    await expect(page).toHaveURL(/\/fr\/dashboard$/, { timeout: 15000 });
    await expect(page.getByTestId('toast')).toContainText(/1 entretien créé/);
  });

  test('Login after registration should work', async ({ page, request, testUser }) => {
    await registerViaApi(request, testUser);

    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(testUser.email, testUser.password);
    // No house yet: P07 (its empty state offers « Commencer » → P05); setup is not forced at login.
    await expect(page).toHaveURL(/\/fr\/dashboard$/, { timeout: 15000 });
  });

  test('Invalid login credentials should fail', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();

    await loginPage.login('nonexistent@test.com', 'WrongPassword!');
    await loginPage.expectLoginError();
  });

  test('Duplicate email registration should fail', async ({ page, request, testUser }) => {
    await registerViaApi(request, testUser);

    const registerPage = new RegisterPage(page);
    await registerPage.goto();
    await page.waitForLoadState('networkidle');
    await registerPage.register('Another', 'User', testUser.email, 'DifferentPass123!');
    await registerPage.expectRegisterError();
  });
});

/** P04 · Invitation and P03 in invitation mode (no setup: P04 → P03/P02 → P09). */
test.describe('Invitation onboarding', () => {
  test('Not signed in: create an account from the invitation, email locked, joins the house', async ({ page, request }) => {
    const owner = await registerViaApi(request, { firstName: 'Marie', lastName: 'Dubois' });
    const houseId = await createHouseViaApi(request, owner.token, 'Maison des Lilas');
    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'CollaboratorRW');

    // State A.
    await page.goto(`/fr/invitations/${invitation.token}`);
    await expect(page.getByRole('heading', { name: 'Marie Dubois vous invite' })).toBeVisible({ timeout: 15000 });
    await expect(page.getByText('Maison des Lilas')).toBeVisible();
    await expect(page.getByText('Rôle : Collaborateur')).toBeVisible();
    await expect(page.getByTestId('invitation-role-description'))
      .toHaveText('Vous pourrez consulter la maison, ajouter des appareils et enregistrer des entretiens.');
    await expect(page.getByTestId('invitation-login'))
      .toHaveAttribute('href', `/fr/login?returnUrl=${encodeURIComponent(`/fr/invitations/${invitation.token}?accept=1`)}`);
    // RGPD art. 14 notice with its privacy link, under the buttons.
    await expect(page.getByTestId('invitation-privacy-notice'))
      .toContainText(/votre adresse e-mail nous a été communiquée par la personne qui vous invite/i);
    await expect(page.locator('a[href="/fr/privacy"][target="_blank"]').first()).toBeVisible();

    await page.getByTestId('invitation-register').click();
    await expect(page).toHaveURL(new RegExp(`/fr/register\\?invitation=${invitation.token}`));

    // P03 invitation mode: no stepper, email locked to the invited address.
    const register = new RegisterPage(page);
    await expect(register.emailInput).toHaveValue(invitation.email);
    await expect(register.emailInput).toHaveAttribute('readonly', '');
    await expect(register.stepper).toHaveCount(0);
    await expect(register.registerButton).toContainText('Créer mon compte et rejoindre');

    await register.register('Paul', 'Invité', null, 'TestPassword123!');
    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseId}$`), { timeout: 15000 });
    await expect(page.getByTestId('toast')).toContainText('Vous avez rejoint Maison des Lilas');
  });

  test('"J\'ai déjà un compte" → login → the invitation is accepted automatically → house', async ({ page, request }) => {
    const owner = await registerViaApi(request, { firstName: 'Marie', lastName: 'Dubois' });
    const houseId = await createHouseViaApi(request, owner.token, 'Maison des Lilas');
    const invitee = await registerViaApi(request, { firstName: 'Paul', lastName: 'Invité' });
    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'Tenant', invitee.email);

    await page.goto(`/fr/invitations/${invitation.token}`);
    await page.getByTestId('invitation-login').click();

    const login = new LoginPage(page);
    await login.login(invitee.email, 'TestPassword123!');
    // Spec P02: back to P04, where the invitation is accepted without a second click → P09.
    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseId}$`), { timeout: 15000 });
    await expect(page.getByTestId('toast')).toContainText('Vous avez rejoint Maison des Lilas');
    await expect(page.getByTestId('house-shared')).toHaveText('Partagée · Locataire');
  });

  test('Auto-accept that fails (invitation for another email) falls back to state B with the error', async ({ page, request }) => {
    const owner = await registerViaApi(request, { firstName: 'Marie', lastName: 'Dubois' });
    const houseId = await createHouseViaApi(request, owner.token, 'Maison des Lilas');
    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'CollaboratorRO', generateTestEmail());
    const someoneElse = await registerViaApi(request, { firstName: 'Léa', lastName: 'Autre' });
    await addRefreshCookie(page.context(), someoneElse.refreshCookie);

    await page.goto(`/fr/invitations/${invitation.token}?accept=1`);
    await expect(page.getByTestId('invitation-error')).toHaveText('Cette invitation a été envoyée à une autre adresse email.', { timeout: 15000 });
    await expect(page.getByTestId('invitation-card')).toHaveAttribute('data-state', 'signed-in');
    await expect(page.getByTestId('invitation-accept')).toBeVisible();
    // The one-shot marker is dropped: a reload shows state B without trying again.
    await expect(page).toHaveURL(new RegExp(`/fr/invitations/${invitation.token}$`));
  });

  test('P04 → "J\'ai déjà un compte" → P02 "Créer un compte" keeps the invitation (P03 invitation mode) → house', async ({ page, request }) => {
    const owner = await registerViaApi(request, { firstName: 'Marie', lastName: 'Dubois' });
    const houseId = await createHouseViaApi(request, owner.token, 'Maison des Lilas');
    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'CollaboratorRW');

    await page.goto(`/fr/invitations/${invitation.token}`);
    await page.getByTestId('invitation-login').click();
    await expect(page).toHaveURL(/\/fr\/login\?returnUrl=/);
    await page.getByRole('link', { name: 'Créer un compte' }).click();
    await expect(page).toHaveURL(/\/fr\/register\?returnUrl=/);

    const register = new RegisterPage(page);
    await expect(page.getByTestId('register-invitation')).toContainText('Maison des Lilas', { timeout: 15000 });
    await expect(register.emailInput).toHaveValue(invitation.email);
    await expect(register.emailInput).toHaveAttribute('readonly', '');
    await expect(register.stepper).toHaveCount(0);
    await expect(register.registerButton).toContainText('Créer mon compte et rejoindre');

    await register.register('Paul', 'Invité', null, 'TestPassword123!');
    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseId}$`), { timeout: 15000 });
    await expect(page.getByTestId('toast')).toContainText('Vous avez rejoint Maison des Lilas');
  });

  test('Signed in: "Refuser" declines and goes to the dashboard; the link is then invalid', async ({ page, request }) => {
    const owner = await registerViaApi(request, { firstName: 'Marie', lastName: 'Dubois' });
    const houseId = await createHouseViaApi(request, owner.token, 'Maison des Lilas');
    const invitee = await registerViaApi(request, { firstName: 'Paul', lastName: 'Invité' });
    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'CollaboratorRO', invitee.email);
    await addRefreshCookie(page.context(), invitee.refreshCookie);

    await page.goto(`/fr/invitations/${invitation.token}`);
    await expect(page.getByTestId('invitation-role-description'))
      .toHaveText('Vous pourrez consulter la maison et son historique.', { timeout: 15000 });
    await page.getByTestId('invitation-decline').click();
    await expect(page).toHaveURL(/\/fr\/dashboard$/, { timeout: 15000 });

    await page.goto(`/fr/invitations/${invitation.token}`);
    await expect(page.getByRole('heading', { name: "Cette invitation n'est plus valide" })).toBeVisible({ timeout: 15000 });
  });

  test('Already a member: straight to the house', async ({ page, request }) => {
    const owner = await registerViaApi(request, { firstName: 'Marie', lastName: 'Dubois' });
    const houseId = await createHouseViaApi(request, owner.token, 'Maison des Lilas');
    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'CollaboratorRW');
    // Register with the invitation token: auto-accepted, the new member has only the shared house.
    const member = await registerViaApi(request, { firstName: 'Paul', lastName: 'Membre', email: invitation.email }, invitation.token);
    expect(member.joinedHouseId).toBe(houseId);

    // Another pending invitation to the same house, opened by the member: no intermediate screen.
    const other = await createInvitationViaApi(request, owner.token, houseId, 'CollaboratorRO');
    await addRefreshCookie(page.context(), member.refreshCookie);
    await page.goto(`/fr/invitations/${other.token}`);
    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseId}$`), { timeout: 15000 });
  });

  test('Unknown token: state C with a way home', async ({ page }) => {
    await page.goto('/fr/invitations/does-not-exist');
    await expect(page.getByRole('heading', { name: "Cette invitation n'est plus valide" })).toBeVisible({ timeout: 15000 });
    await expect(page.getByRole('link', { name: "Aller à l'accueil" })).toHaveAttribute('href', '/fr');
  });

  test('Registering with an invitation for another email is refused', async ({ page, request }) => {
    const owner = await registerViaApi(request, { firstName: 'Marie', lastName: 'Dubois' });
    const houseId = await createHouseViaApi(request, owner.token);
    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'CollaboratorRW', generateTestEmail());

    // The API rejects a mismatching email even if the UI lock were bypassed.
    const res = await request.post(
      `${process.env.API_URL || 'http://localhost:5203'}/api/v1/auth/register?invitationToken=${invitation.token}`,
      { data: { firstName: 'X', lastName: 'Y', email: generateTestEmail(), password: 'TestPassword123!', consentAccepted: true } },
    );
    expect(res.status()).toBe(400);
    expect((await res.json()).code).toBe('invitation_email_mismatch');
  });
});
