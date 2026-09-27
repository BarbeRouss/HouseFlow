import { Page, Locator, expect } from '@playwright/test';

/**
 * Settings page — GDPR self-service sections (profile / my data / delete account).
 */
export class SettingsPage {
  readonly page: Page;

  // Profile (Art. 16)
  readonly firstNameInput: Locator;
  readonly lastNameInput: Locator;
  readonly emailInput: Locator;
  readonly saveProfileButton: Locator;
  readonly profileSuccess: Locator;
  readonly profileError: Locator;

  // My data (Art. 15 + 20)
  readonly exportJsonButton: Locator;
  readonly exportCsvButton: Locator;
  readonly exportSuccess: Locator;
  readonly exportError: Locator;

  // Delete account (Art. 17)
  readonly openDeleteButton: Locator;
  readonly acknowledgeCheckbox: Locator;
  readonly passwordInput: Locator;
  readonly confirmDeleteButton: Locator;
  readonly deleteError: Locator;

  constructor(page: Page) {
    this.page = page;

    this.firstNameInput = page.getByTestId('profile-first-name');
    this.lastNameInput = page.getByTestId('profile-last-name');
    this.emailInput = page.getByTestId('profile-email');
    this.saveProfileButton = page.getByTestId('save-profile');
    this.profileSuccess = page.getByTestId('profile-success');
    this.profileError = page.getByTestId('profile-error');

    this.exportJsonButton = page.getByTestId('export-json');
    this.exportCsvButton = page.getByTestId('export-csv');
    this.exportSuccess = page.getByTestId('export-success');
    this.exportError = page.getByTestId('export-error');

    this.openDeleteButton = page.getByTestId('open-delete-account');
    this.acknowledgeCheckbox = page.getByTestId('delete-acknowledge');
    this.passwordInput = page.getByTestId('delete-password');
    this.confirmDeleteButton = page.getByTestId('confirm-delete-account');
    this.deleteError = page.getByTestId('delete-error');
  }

  async goto() {
    await this.page.goto('/fr/settings');
    await this.page.waitForLoadState('networkidle');
    await expect(this.saveProfileButton).toBeVisible();
  }

  /** Opens Settings through the header user menu, as a user would. */
  async gotoViaHeaderMenu() {
    await this.page.locator('header').getByText('TU').click();
    await this.page.getByRole('link', { name: /paramètres|settings/i }).click();
    await this.page.waitForURL(/\/fr\/settings$/);
    await expect(this.saveProfileButton).toBeVisible();
  }

  async deleteAccount(password: string) {
    await this.openDeleteButton.click();
    await this.acknowledgeCheckbox.check();
    await this.passwordInput.fill(password);
    await this.confirmDeleteButton.click();
  }
}
