import { Page, Locator, expect } from '@playwright/test';

export class HousePage {
  readonly page: Page;
  readonly houseNameInput: Locator;
  readonly addressInput: Locator;
  readonly zipCodeInput: Locator;
  readonly cityInput: Locator;
  readonly saveButton: Locator;
  readonly cancelButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.houseNameInput = page.getByPlaceholder(/my house|ma maison/i);
    this.addressInput = page.getByPlaceholder(/123 main street|123 rue/i);
    this.zipCodeInput = page.getByPlaceholder(/12345/i);
    this.cityInput = page.getByPlaceholder(/paris|city/i);
    this.saveButton = page.getByRole('button', { name: /save|enregistrer/i });
    this.cancelButton = page.getByRole('button', { name: /cancel|annuler/i });
  }

  async goto() {
    await this.page.goto('/fr/houses/new');
  }

  async createHouse(name: string, address: string, zipCode: string, city: string) {
    await this.houseNameInput.fill(name);
    await this.addressInput.fill(address);
    await this.zipCodeInput.fill(zipCode);
    await this.cityInput.fill(city);
    await this.saveButton.click();
  }

  async expectCreateSuccess() {
    // Should navigate to house detail page
    await expect(this.page).toHaveURL(/.*houses\/[a-f0-9-]+$/);
  }
}
