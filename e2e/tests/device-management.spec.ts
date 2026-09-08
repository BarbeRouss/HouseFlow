import { test, expect } from '../fixtures/auth';
import { DashboardPage } from '../pages/dashboard-page';
import { HousePage } from '../pages/house-page';

test.describe('User Flow 2: Device Management', () => {
  test('Add device with auto maintenance type configuration', async ({ authenticatedPage: page }) => {
    // User already has "Ma Maison" auto-created and is on the house page

    // ÉTAPE 1: Add a device to the auto-created house
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+\/devices\/new/);

    await page.getByPlaceholder(/chaudière/i).fill('Chaudière Sous-sol');
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: 'Chaudière Gaz' }).click();
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // ÉTAPE 2: Verify device was created and redirected to house page
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);
    await expect(page.getByText('Chaudière Sous-sol')).toBeVisible();
  });

  test('Show validation error when creating device without type', async ({ authenticatedPage: page }) => {
    // Navigate to device creation page
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+\/devices\/new/);

    // Fill only the name, skip type selection
    await page.getByPlaceholder(/chaudière/i).fill('Appareil Sans Type');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // Should stay on the form and show a validation error for the type field
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+\/devices\/new/);
    await expect(page.getByText(/veuillez sélectionner un type|please select a device type/i)).toBeVisible();
  });

  test('View all devices for a house', async ({ authenticatedPage: page }) => {
    // User already has "Ma Maison" auto-created and is on the house page

    // Add multiple devices to the auto-created house
    const devices = [
      { name: 'Chaudière', type: 'Chaudière Gaz' },
      { name: 'Toiture', type: 'Toiture' },
      { name: 'Alarme', type: 'Alarme' },
    ];

    for (const device of devices) {
      await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
      await page.getByPlaceholder(/chaudière/i).fill(device.name);
      await page.getByRole('combobox').click();
      await page.getByRole('option', { name: device.type }).click();
      await page.getByRole('button', { name: /save|enregistrer/i }).click();
      await page.waitForURL(/\/fr\/houses\/[a-f0-9-]+$/);
    }

    // Verify all devices are visible
    for (const device of devices) {
      await expect(page.getByRole('heading', { name: device.name })).toBeVisible();
    }
  });
});
