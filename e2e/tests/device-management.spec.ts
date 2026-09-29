import { test, expect } from '../fixtures/auth';
import { HousePage } from '../pages/house-page';

// M2 (add a device from P09) and the P09 device list (C4 rows). The fixture lands on the empty
// P09 of « Ma maison » (owner).
test.describe('User Flow 2: Device Management', () => {
  test('Add a catalogue device: M2 → P10, then its row on the house page', async ({ authenticatedPage: page }) => {
    const houses = new HousePage(page);
    const houseUrl = page.url();

    await houses.openAddDevice();
    const modal = houses.deviceModal;
    await expect(modal.getByRole('heading', { name: /nouvel appareil/i })).toBeVisible();

    // Type required: « Ajouter » disabled until a chip is chosen; the name follows the type label.
    await expect(modal.getByTestId('device-modal-submit')).toBeDisabled();
    await modal.getByTestId('device-type-gasBoiler').click();
    await expect(modal.getByTestId('device-type-gasBoiler')).toHaveAttribute('aria-checked', 'true');
    await expect(modal.getByTestId('device-name')).toHaveValue('Chaudière gaz');
    await modal.getByTestId('device-type-heatPump').click();
    await expect(modal.getByTestId('device-name')).toHaveValue('Pompe à chaleur');

    // Once edited, the name no longer follows the type.
    await modal.getByTestId('device-name').fill('Chaudière Sous-sol');
    await modal.getByTestId('device-type-gasBoiler').click();
    await expect(modal.getByTestId('device-name')).toHaveValue('Chaudière Sous-sol');

    await modal.getByTestId('device-brand').fill('Viessmann');
    await modal.getByTestId('device-model').fill('Vitodens 200');

    // « Dernier entretien »: a year without a month keeps « Ajouter » disabled. Serviced this month
    // → next due in a year (À jour).
    const now = new Date();
    await modal.getByTestId('device-last-maintenance-year').selectOption(String(now.getFullYear()));
    await expect(modal.getByTestId('device-last-maintenance-hint')).toHaveText(/choisissez le mois/i);
    await expect(modal.getByTestId('device-modal-submit')).toBeDisabled();
    await modal.getByTestId('device-last-maintenance-month').selectOption(String(now.getMonth() + 1));
    await expect(modal.getByTestId('device-modal-submit')).toBeEnabled();

    await modal.getByTestId('device-modal-submit').click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);
    await expect(page.getByRole('heading', { name: 'Chaudière Sous-sol', level: 1 })).toBeVisible();

    // Back on P09: the row shows brand + model (up to date) and a date, no percentage.
    await page.goto(houseUrl);
    const row = houses.deviceRow('Chaudière Sous-sol');
    await expect(row).toBeVisible();
    await expect(row.getByTestId('device-row-subtitle')).toHaveText('Viessmann Vitodens 200');
    await expect(page.getByText(/%/)).toHaveCount(0);
  });

  test('« Autre » hides « Dernier entretien » and creates a device without maintenance', async ({ authenticatedPage: page }) => {
    const houses = new HousePage(page);
    const houseUrl = page.url();

    await houses.openAddDevice();
    await houses.deviceModal.getByTestId('device-type-gasBoiler').click();
    await expect(houses.deviceModal.getByTestId('device-last-maintenance')).toBeVisible();
    await houses.deviceModal.getByTestId('device-type-other').click();
    await expect(houses.deviceModal.getByTestId('device-last-maintenance')).toHaveCount(0);
    await expect(houses.deviceModal.getByTestId('device-name')).toHaveValue('Autre');
    await houses.deviceModal.getByTestId('device-name').fill('Pompe hydrophore');
    await houses.deviceModal.getByTestId('device-modal-submit').click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);

    await page.goto(houseUrl);
    await expect(houses.deviceRow('Pompe hydrophore')).toContainText(/aucun entretien/i);
  });

  test('Cancel closes M2 without creating anything', async ({ authenticatedPage: page }) => {
    const houses = new HousePage(page);

    await houses.openAddDevice();
    await houses.deviceModal.getByTestId('device-type-vmc').click();
    await houses.deviceModal.getByRole('button', { name: /annuler/i }).click();
    await expect(houses.deviceModal).toHaveCount(0);
    await expect(page.getByTestId('devices-empty')).toBeVisible();
  });

  test('Devices are sorted by status, the urgent ones naming their maintenance', async ({ authenticatedPage: page }) => {
    const houses = new HousePage(page);
    const houseUrl = page.url();
    const now = new Date();

    // Up to date: serviced this month → next due in about a year.
    await houses.addDevice({ type: 'gasBoiler', name: 'Chaudière', lastMaintenance: { year: now.getFullYear(), month: now.getMonth() + 1 } });
    await page.goto(houseUrl);
    // Due: « Je ne sais pas » → due 30 days after creation (À faire).
    await houses.addDevice({ type: 'smokeDetector', name: 'Détecteur', lastMaintenance: 'unknown' });
    await page.goto(houseUrl);
    // Overdue: swept two years ago, yearly.
    await houses.addDevice({ type: 'woodStove', name: 'Poêle', lastMaintenance: { year: now.getFullYear() - 2, month: 1 } });
    await page.goto(houseUrl);

    const rows = houses.deviceRows();
    await expect(rows).toHaveCount(3);
    await expect(rows.nth(0)).toHaveAttribute('data-status', 'overdue');
    await expect(rows.nth(0).getByTestId('device-row-subtitle')).toHaveText('Ramonage');
    await expect(rows.nth(0)).toContainText('En retard');
    await expect(rows.nth(1)).toHaveAttribute('data-status', 'pending');
    await expect(rows.nth(1).getByTestId('device-row-subtitle')).toHaveText('Test');
    await expect(rows.nth(1)).toContainText('À faire');
    await expect(rows.nth(2)).toHaveAttribute('data-status', 'up_to_date');
    await expect(rows.nth(2).getByTestId('device-row-subtitle')).toHaveText('Chaudière gaz');

    // The house ring counts « à jour » maintenances: 1 out of 3.
    await expect(page.getByTestId('progress-ring')).toContainText('1/3');

    // A row is a link to P10.
    await houses.deviceRow('Poêle').click({ position: { x: 5, y: 5 } });
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);
  });
});
