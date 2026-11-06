import { Page, Locator, expect } from '@playwright/test';

/** P06 / M2 catalogue chip ids (data-testid `device-type-{id}`), see Features/Shared/DeviceCatalog.cs. */
export type DeviceCatalogId =
  | 'gasBoiler'
  | 'smokeDetector'
  | 'woodStove'
  | 'vmc'
  | 'heatPump'
  | 'waterHeater'
  | 'airConditioner'
  | 'alarm'
  | 'pressurePump'
  | 'other';

/** « Dernier entretien » (R2): year + month, « Plus ancien » or « Je ne sais pas » (default). */
export type LastMaintenanceInput = { year: number; month: number } | 'older' | 'unknown';

export type NewDevice = {
  type: DeviceCatalogId;
  /** Default: the name pre-filled from the type label. */
  name?: string;
  brand?: string;
  model?: string;
  lastMaintenance?: LastMaintenanceInput;
};

/**
 * P08 (houses list), P09 (house page), M1 (house modal) and M2 (device modal).
 * The pages /houses/new and /houses/{id}/devices/new no longer exist: creation happens in modals.
 */
export class HousePage {
  readonly page: Page;
  readonly houseModal: Locator;
  readonly deviceModal: Locator;

  constructor(page: Page) {
    this.page = page;
    this.houseModal = page.getByTestId('house-modal');
    this.deviceModal = page.getByTestId('device-modal');
  }

  /** P08. */
  async gotoList() {
    await this.page.goto('/fr/houses');
    await expect(this.page.getByTestId('houses-page')).toBeVisible();
  }

  /** P08 « Ajouter une maison » → M1 → P09 of the new house. Returns its id. */
  async createHouse(name: string, address?: string): Promise<string> {
    await this.gotoList();
    await this.page.getByTestId('add-house').click();
    await expect(this.houseModal).toBeVisible();
    await this.houseModal.getByTestId('house-name').fill(name);
    if (address) await this.houseModal.getByTestId('house-address').fill(address);
    await this.houseModal.getByTestId('house-modal-submit').click();
    await this.page.waitForURL(/\/fr\/houses\/[a-f0-9-]+$/, { timeout: 15000 });
    await expect(this.page.getByRole('heading', { name, level: 1 })).toBeVisible();
    return this.page.url().split('/').pop()!;
  }

  /** Opens M2 from the current P09. */
  async openAddDevice() {
    await this.page.getByTestId('add-device').first().click();
    await expect(this.deviceModal).toBeVisible();
  }

  /** P09 « Ajouter un appareil » → M2 → P10 of the new device. Returns its id. */
  async addDevice(device: NewDevice): Promise<string> {
    await this.openAddDevice();
    await this.deviceModal.getByTestId(`device-type-${device.type}`).click();
    if (device.name !== undefined) await this.deviceModal.getByTestId('device-name').fill(device.name);
    if (device.brand) await this.deviceModal.getByTestId('device-brand').fill(device.brand);
    if (device.model) await this.deviceModal.getByTestId('device-model').fill(device.model);
    if (device.lastMaintenance && device.type !== 'other') await this.setLastMaintenance(device.lastMaintenance);
    await this.deviceModal.getByTestId('device-modal-submit').click();
    await this.page.waitForURL(/\/fr\/devices\/[a-f0-9-]+$/, { timeout: 15000 });
    return this.page.url().split('/').pop()!;
  }

  async setLastMaintenance(value: LastMaintenanceInput) {
    const year = this.deviceModal.getByTestId('device-last-maintenance-year');
    const month = this.deviceModal.getByTestId('device-last-maintenance-month');
    if (value === 'older' || value === 'unknown') {
      await year.selectOption(value);
      return;
    }
    await year.selectOption(String(value.year));
    await month.selectOption(String(value.month));
  }

  /** C4 rows of P09, in display order. */
  deviceRows(): Locator {
    return this.page.getByTestId('device-row');
  }

  deviceRow(name: string): Locator {
    return this.deviceRows().filter({ has: this.page.getByTestId('device-row-link').getByText(name, { exact: true }) });
  }

  /** C4 house cards of P08, in display order. */
  houseCards(): Locator {
    return this.page.getByTestId('house-card');
  }

  houseCard(name: string): Locator {
    return this.houseCards().filter({ has: this.page.getByTestId('house-card-link').getByText(name, { exact: true }) });
  }

  /** P09 ⋯ menu (owner only). */
  async openHouseMenu() {
    await this.page.getByTestId('house-menu').click();
  }
}
