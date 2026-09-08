import { Page, Locator, expect } from '@playwright/test';

export class DashboardPage {
  readonly page: Page;
  readonly welcomeHeading: Locator;
  readonly addHouseButton: Locator;
  readonly houseCards: Locator;

  constructor(page: Page) {
    this.page = page;
    this.welcomeHeading = page.getByRole('heading', { name: /welcome|bienvenue/i });
    this.addHouseButton = page.getByRole('link', { name: /add house|ajouter une maison/i }).first();
    this.houseCards = page.locator('[class*="Card"]').filter({ hasText: /view details|voir les détails/i });
  }

  async goto() {
    await this.page.goto('/fr/dashboard');
  }

  async clickAddHouse() {
    // Wait for the element to be visible and stable (webkit hydration issue)
    await this.addHouseButton.waitFor({ state: 'visible', timeout: 10000 });
    // Add a small delay to ensure React hydration is complete
    await this.page.waitForTimeout(500);
    await this.addHouseButton.click();
  }

  async expectHouseCount(count: number) {
    if (count === 0) {
      await expect(this.page.getByText(/no houses yet|aucune maison/i)).toBeVisible();
    } else {
      await expect(this.houseCards).toHaveCount(count);
    }
  }

  async clickHouse(houseName: string) {
    await this.page.getByRole('link', { name: new RegExp(houseName, 'i') }).click();
  }
}
