import { test, expect } from '../fixtures/auth';
import { SettingsPage } from '../pages/settings-page';
import { LoginPage } from '../pages/login-page';

/**
 * GDPR self-service rights, end to end:
 * - Art. 15 + 20 (access / portability): export download
 * - Art. 17 (erasure): account deletion
 * - Art. 16 (rectification): profile editing
 */
test.describe('RGPD — mon compte', () => {
  test("l'export JSON déclenche le téléchargement du fichier de données", async ({ authenticatedPage: page }) => {
    const settings = new SettingsPage(page);
    await settings.goto();

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      settings.exportJsonButton.click(),
    ]);

    expect(download.suggestedFilename()).toMatch(/^houseflow-data-export-\d{4}-\d{2}-\d{2}\.json$/);
    await expect(settings.exportSuccess).toBeVisible();

    // Un seul export par heure (Art. 12(5)) : le second appel est refusé avec un
    // message explicite plutôt qu'une erreur générique.
    await settings.exportJsonButton.click();
    await expect(settings.exportError).toBeVisible();
    await expect(settings.exportError).toContainText(/moins d'une heure/i);
  });

  test('la rectification du prénom se reflète dans le header après rechargement', async ({ authenticatedPage: page }) => {
    const settings = new SettingsPage(page);
    await settings.goto();

    await settings.firstNameInput.fill('Rectifie');
    await settings.saveProfileButton.click();
    await expect(settings.profileSuccess).toBeVisible();

    await page.reload();
    await page.waitForLoadState('networkidle');

    await expect(page.locator('header').getByText('Rectifie')).toBeVisible();
    await expect(settings.firstNameInput).toHaveValue('Rectifie');
  });

  test('la suppression du compte est confirmée puis empêche toute reconnexion', async ({ authenticatedPage: page, testUser }) => {
    const settings = new SettingsPage(page);
    await settings.gotoViaHeaderMenu();

    // La confirmation est désactivée tant que la case n'est pas cochée.
    await settings.openDeleteButton.click();
    await expect(settings.confirmDeleteButton).toBeDisabled();
    await settings.acknowledgeCheckbox.check();
    await expect(settings.confirmDeleteButton).toBeDisabled();

    // Un mot de passe erroné est refusé sans supprimer quoi que ce soit.
    await settings.passwordInput.fill('MauvaisMotDePasse123!');
    await settings.confirmDeleteButton.click();
    await expect(settings.deleteError).toBeVisible();

    await settings.passwordInput.fill(testUser.password);
    await Promise.all([
      page.waitForURL(/\/fr\/login$/, { timeout: 15000 }),
      settings.confirmDeleteButton.click(),
    ]);

    const login = new LoginPage(page);
    await login.login(testUser.email, testUser.password);
    await login.expectLoginError();
  });
});
