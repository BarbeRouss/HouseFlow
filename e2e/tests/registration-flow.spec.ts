import { test, expect, createHouseViaApi, registerViaApi, addRefreshCookie, generateTestEmail } from '../fixtures/auth';
import { RegisterPage } from '../pages/register-page';
import { SetupPage } from '../pages/setup-page';
import { LoginPage } from '../pages/login-page';

const PASSWORD = 'TestPassword123!';

/**
 * Onboarding without invitation: P03 Inscription → P05 Setup maison → P06 Setup équipements → P07.
 * Registration no longer creates a house: P05 is the only place where the first one is created.
 */
test.describe('Registration and setup', () => {
  test('Register → P05 → P06 → create 2 maintenance tasks → dashboard with toast', async ({ page }) => {
    const register = new RegisterPage(page);
    await register.goto();
    await page.waitForLoadState('networkidle');

    // Step 1/3, "Continuer" disabled until the terms are accepted.
    await expect(register.stepper).toBeVisible();
    await expect(register.registerButton).toBeDisabled();
    await expect(register.registerButton).toContainText('Continuer');

    await register.register('Jean', 'Dupont', generateTestEmail(), PASSWORD);
    await register.expectRegisterSuccess();

    // P05: step 2/3, name prefilled « Ma maison ».
    const setup = new SetupPage(page);
    await expect(page.getByRole('heading', { name: /comment s'appelle votre maison/i })).toBeVisible();
    await expect(setup.houseName).toHaveValue('Ma maison');
    const houseId = await setup.createHouse('Maison des Lilas', '12 rue des Lilas');

    // P06: no chip selected, preview hidden, create disabled.
    await expect(page.getByRole('heading', { name: /qu'y a-t-il dans maison des lilas/i })).toBeVisible();
    await expect(setup.preview).toHaveCount(0);
    await expect(setup.createTasks).toBeDisabled();

    await setup.chip('gasBoiler').click();
    await setup.chip('smokeDetector').click();
    await expect(setup.chip('gasBoiler')).toHaveAttribute('aria-pressed', 'true');
    await expect(setup.createTasks).toContainText('Créer mes 2 entretiens');

    // A year without a month blocks the creation (« Choisissez le mois »).
    const lastYear = String(new Date().getFullYear() - 1);
    await setup.yearSelect('gasBoiler').selectOption(lastYear);
    await expect(setup.createTasks).toBeDisabled();
    await expect(page.getByTestId('setup-last-gasBoiler-hint')).toBeVisible();
    await setup.monthSelect('gasBoiler').selectOption('3');
    await expect(setup.createTasks).toBeEnabled();

    // Live preview: one line per selected device.
    await expect(setup.previewRows).toHaveCount(2);

    await setup.createTasks.click();
    await expect(page).toHaveURL(/\/fr\/dashboard$/, { timeout: 15000 });
    await expect(page.getByTestId('toast')).toContainText(/2 entretiens créés/);

    // The house exists with its two devices.
    await page.goto(`/fr/houses/${houseId}`);
    await expect(page.getByRole('heading', { name: 'Maison des Lilas' })).toBeVisible({ timeout: 10000 });
  });

  test('"Passer" on P05 goes to the dashboard without creating a house', async ({ page }) => {
    const register = new RegisterPage(page);
    await register.goto();
    await page.waitForLoadState('networkidle');
    await register.register('Jean', 'Dupont', generateTestEmail(), PASSWORD);
    await register.expectRegisterSuccess();

    await new SetupPage(page).skip.click();
    await expect(page).toHaveURL(/\/fr\/dashboard$/, { timeout: 10000 });
  });

  test('"Passer" on P06 goes to the created house', async ({ page }) => {
    const register = new RegisterPage(page);
    await register.goto();
    await page.waitForLoadState('networkidle');
    await register.register('Jean', 'Dupont', generateTestEmail(), PASSWORD);
    await register.expectRegisterSuccess();

    const setup = new SetupPage(page);
    const houseId = await setup.createHouse();
    await setup.skip.click();
    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseId}$`), { timeout: 10000 });
  });

  test('P05 sends a user who already owns a house to the dashboard', async ({ page, request }) => {
    const owner = await registerViaApi(request, { firstName: 'Own', lastName: 'Er' });
    await createHouseViaApi(request, owner.token);
    await addRefreshCookie(page.context(), owner.refreshCookie);

    await page.goto('/fr/setup/house');
    await expect(page).toHaveURL(/\/fr\/dashboard$/, { timeout: 15000 });
  });

  test('Browser back button after registration does not return to register page', async ({ page }) => {
    const register = new RegisterPage(page);
    await register.goto();
    await page.waitForLoadState('networkidle');
    await register.register('Jean', 'Dupont', generateTestEmail(), PASSWORD);
    await register.expectRegisterSuccess();

    await page.goBack();
    await page.waitForLoadState('networkidle');

    // The /register entry was replaced; a signed-in user is never shown the form again (R6).
    expect(page.url()).not.toMatch(/\/register/);
  });

  test('User can login after registration', async ({ page }) => {
    const email = generateTestEmail();
    const register = new RegisterPage(page);
    await register.goto();
    await page.waitForLoadState('networkidle');
    await register.register('Test', 'User', email, PASSWORD);
    await register.expectRegisterSuccess();
    await new SetupPage(page).createHouse();

    // Simulate a closed browser: drop the refresh cookie; the in-memory token goes with the reload.
    await page.context().clearCookies();

    const login = new LoginPage(page);
    await login.goto();
    await login.login(email, PASSWORD);
    await expect(page).toHaveURL(/\/fr\/dashboard$/, { timeout: 10000 });
    await expect(page.getByText(/ma maison/i).first()).toBeVisible({ timeout: 10000 });
  });

  test('Duplicate email shows the message under the email field with a login link', async ({ page, request }) => {
    const existing = await registerViaApi(request, { firstName: 'Taken', lastName: 'User' });

    const register = new RegisterPage(page);
    await register.goto();
    await page.waitForLoadState('networkidle');
    await register.register('Another', 'User', existing.email, 'DifferentPass123!');

    await expect(register.emailError).toContainText('Un compte existe déjà avec cet email.');
    await expect(register.emailError.getByRole('link', { name: /se connecter/i })).toBeVisible();
    await expect(page).toHaveURL(/\/fr\/register/);
  });
});
