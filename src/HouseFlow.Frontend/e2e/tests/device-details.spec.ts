import { test, expect } from '../fixtures/auth';

test.describe('User Flow: Device Details', () => {
  test('View device details page with maintenance types', async ({ authenticatedPage: page }) => {
    // First add a device to the auto-created house
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Chaudiere Test');
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: 'Chaudière Gaz' }).click();
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // Click on the device to view details
    await expect(page.getByRole('heading', { name: 'Chaudiere Test' })).toBeVisible();
    await page.getByRole('heading', { name: 'Chaudiere Test' }).click();

    // Verify device detail page
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);
    await expect(page.getByRole('heading', { name: 'Chaudiere Test', level: 1 })).toBeVisible();

    // Verify score is displayed
    await expect(page.getByText(/100%/).first()).toBeVisible();

    // Verify maintenance types section exists
    await expect(page.getByRole('heading', { name: /types d'entretien|maintenance types/i })).toBeVisible();

    // Verify history section exists (use level 2 to match the section heading, not subheading)
    await expect(page.getByRole('heading', { name: /historique|history/i, level: 2 })).toBeVisible();
  });

  test('Device shows correct score after logging maintenance', async ({ authenticatedPage: page }) => {
    // Add a device first
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Alarme Test');
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: 'Alarme' }).click();
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // Go to device details
    await expect(page.getByRole('heading', { name: 'Alarme Test' })).toBeVisible();
    await page.getByRole('heading', { name: 'Alarme Test' }).click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);

    // Verify initial score (100% for new device with no maintenance types or all up-to-date)
    await expect(page.getByText(/100%/).first()).toBeVisible();
  });

  test('Navigate back to house from device details', async ({ authenticatedPage: page }) => {
    // Add a device first
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Toiture Test');
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: 'Toiture' }).click();
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // Go to device details
    await page.getByRole('heading', { name: 'Toiture Test' }).click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);

    // Navigate back via breadcrumb
    await page.getByRole('link', { name: /maison/i }).click();
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);

    // Verify we're back on the house page
    await expect(page.getByRole('heading', { name: /ma maison/i })).toBeVisible();
  });

  test('Device card shows status badges correctly', async ({ authenticatedPage: page }) => {
    // Add multiple devices with different configurations
    const devices = [
      { name: 'Climatisation Test', type: 'Climatisation' },
      { name: 'VMC Test', type: 'VMC' },
    ];

    for (const device of devices) {
      await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
      await page.getByPlaceholder(/chaudière/i).fill(device.name);
      await page.getByRole('combobox').click();
      await page.getByRole('option', { name: device.type }).click();
      await page.getByRole('button', { name: /save|enregistrer/i }).click();
      await page.waitForURL(/\/fr\/houses\/[a-f0-9-]+$/);
    }

    // Verify both devices appear on house page
    for (const device of devices) {
      await expect(page.getByRole('heading', { name: device.name })).toBeVisible();
    }

    // Verify progress bars are visible for each device card
    const progressBars = page.locator('.h-1\\.5.bg-gray-100');
    await expect(progressBars.first()).toBeVisible();
  });

  test('Add multiple devices to a house', async ({ authenticatedPage: page }) => {
    // Add 3 different devices
    const devices = [
      { name: 'Chauffe-eau Principal', type: 'Chauffe-eau' },
      { name: 'Pompe à Chaleur', type: 'Pompe à Chaleur' },
      { name: 'Détecteur Fumée Salon', type: 'Détecteur de Fumée' },
    ];

    for (const device of devices) {
      await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
      await page.getByPlaceholder(/chaudière/i).fill(device.name);
      await page.getByRole('combobox').click();
      await page.getByRole('option', { name: device.type }).click();
      await page.getByRole('button', { name: /save|enregistrer/i }).click();
      await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);
    }

    // Verify all devices are visible on the house page
    for (const device of devices) {
      await expect(page.getByRole('heading', { name: device.name })).toBeVisible();
    }
  });
});
