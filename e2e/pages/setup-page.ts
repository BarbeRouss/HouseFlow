import { Page, Locator, expect } from '@playwright/test';

/** P05 · Setup maison and P06 · Setup équipements. */
export class SetupPage {
  readonly page: Page;
  readonly houseName: Locator;
  readonly houseAddress: Locator;
  readonly houseContinue: Locator;
  readonly skip: Locator;
  readonly createTasks: Locator;
  readonly preview: Locator;
  readonly previewRows: Locator;

  constructor(page: Page) {
    this.page = page;
    this.houseName = page.getByTestId('setup-house-name');
    this.houseAddress = page.getByTestId('setup-house-address');
    this.houseContinue = page.getByTestId('setup-house-continue');
    this.skip = page.getByTestId('setup-skip');
    this.createTasks = page.getByTestId('setup-devices-create');
    this.preview = page.getByTestId('setup-preview');
    this.previewRows = page.getByTestId('setup-preview-row');
  }

  /** P05 → P06; returns the created house id (from ?house=). */
  async createHouse(name?: string, address?: string): Promise<string> {
    await expect(this.houseName).toBeVisible();
    if (name !== undefined) await this.houseName.fill(name);
    if (address !== undefined) await this.houseAddress.fill(address);
    await this.houseContinue.click();
    await expect(this.page).toHaveURL(/\/fr\/setup\/devices\?house=[a-f0-9-]+/, { timeout: 15000 });
    const id = new URL(this.page.url()).searchParams.get('house');
    if (!id) throw new Error(`No house id in ${this.page.url()}`);
    return id;
  }

  /** Catalogue chip by DeviceCatalog id (gasBoiler, smokeDetector, woodStove, vmc, heatPump, waterHeater). */
  chip(id: string): Locator {
    return this.page.getByTestId(`setup-chip-${id}`);
  }

  row(id: string): Locator {
    return this.page.getByTestId(`setup-row-${id}`);
  }

  yearSelect(id: string): Locator {
    return this.page.getByTestId(`setup-last-${id}-year`);
  }

  monthSelect(id: string): Locator {
    return this.page.getByTestId(`setup-last-${id}-month`);
  }

  /** « Nom de l'entretien » of a checked card (prefilled from the catalogue, 100 max). */
  taskName(id: string): Locator {
    return this.page.getByTestId(`setup-task-name-${id}`);
  }

  /** « Fréquence » of a checked card: option values are months (3, 6, 12, 24). */
  frequency(id: string): Locator {
    return this.page.getByTestId(`setup-frequency-${id}`);
  }
}
