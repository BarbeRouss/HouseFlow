import { Page, Locator, expect } from '@playwright/test';

/** P03 · Inscription (standard: stepper 1/3 → P05; ?invitation=: email locked → P09). */
export class RegisterPage {
  readonly page: Page;
  readonly firstNameInput: Locator;
  readonly lastNameInput: Locator;
  readonly emailInput: Locator;
  readonly passwordInput: Locator;
  readonly acceptTermsCheckbox: Locator;
  readonly registerButton: Locator;
  readonly loginLink: Locator;
  readonly errorMessage: Locator;
  readonly emailError: Locator;
  readonly stepper: Locator;

  constructor(page: Page) {
    this.page = page;
    this.firstNameInput = page.getByPlaceholder('Jean');
    this.lastNameInput = page.getByPlaceholder('Dupont');
    this.emailInput = page.getByPlaceholder('you@example.com');
    this.passwordInput = page.locator('input[type="password"]');
    this.acceptTermsCheckbox = page.locator('#acceptTerms');
    // "Continuer" (standard) or "Créer mon compte et rejoindre" (invitation).
    this.registerButton = page.getByTestId('register-submit');
    this.loginLink = page.getByRole('link', { name: /sign in|se connecter/i });
    // Form-level error, or the message under the email field (email taken, invitation mismatch).
    this.errorMessage = page.locator('[data-testid="register-error"], [data-testid="register-email-error"]');
    this.emailError = page.getByTestId('register-email-error');
    this.stepper = page.getByTestId('stepper');
  }

  async goto(query = '') {
    await this.page.goto(`/fr/register${query}`);
  }

  async fill(firstName: string, lastName: string, email: string | null, password: string) {
    await this.firstNameInput.click();
    await this.firstNameInput.pressSequentially(firstName, { delay: 30 });
    await expect(this.firstNameInput).toHaveValue(firstName);

    await this.lastNameInput.click();
    await this.lastNameInput.pressSequentially(lastName, { delay: 30 });
    await expect(this.lastNameInput).toHaveValue(lastName);

    // null = keep the locked invitation email.
    if (email !== null) {
      await this.emailInput.click();
      await this.emailInput.pressSequentially(email, { delay: 30 });
      await expect(this.emailInput).toHaveValue(email);
    }

    await this.passwordInput.click();
    await this.passwordInput.pressSequentially(password, { delay: 30 });
    await expect(this.passwordInput).toHaveValue(password);

    // RGPD — case « J'accepte les Conditions générales d'utilisation », non pré-cochée :
    // sans elle le bouton reste désactivé et le backend refuserait l'inscription (400).
    await this.acceptTermsCheckbox.check();
    await expect(this.acceptTermsCheckbox).toBeChecked();
  }

  async register(firstName: string, lastName: string, email: string | null, password: string) {
    await this.fill(firstName, lastName, email, password);
    await this.registerButton.click();
  }

  /** P03 → P05 (registration creates no house). */
  async expectRegisterSuccess() {
    await expect(this.page).toHaveURL(/\/fr\/setup\/house$/, { timeout: 15000 });
  }

  async expectRegisterError() {
    await expect(this.errorMessage).toBeVisible();
  }
}
