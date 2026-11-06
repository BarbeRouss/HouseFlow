import { test, expect } from '@playwright/test';
import { createDevice, createHouse, createType, monthsAgo, openAs, registerUser } from '../fixtures/maintenance-seed';

/** P07 · Accueil: header counters (R3), grouped C3 rows, « Mes maisons », empty / all-up-to-date states. */
test.describe('Home (P07)', () => {
  test('No house → only the « Commencer » block, to P05', async ({ page, request }) => {
    const s = await registerUser(request);
    await openAs(page, s, '/fr/dashboard');

    const empty = page.getByTestId('dashboard-empty');
    await expect(empty.getByRole('heading', { name: 'Ajoutez votre maison' })).toBeVisible();
    await expect(empty).toContainText('Choisissez vos équipements, on calcule les échéances.');
    await expect(page.getByTestId('dashboard-start')).toHaveAttribute('href', '/fr/setup/house');
    await expect(page.getByTestId('dashboard-tasks')).toHaveCount(0);
  });

  test('Tasks to handle grouped « En retard » then « À faire prochainement », header counters', async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s, 'Maison des Lilas');
    const stove = await createDevice(request, s, houseId, { name: 'Poêle à bois', type: 'Poêle à Bois' });
    const boiler = await createDevice(request, s, houseId, { name: 'Chaudière gaz', type: 'Chaudière Gaz' });
    await createType(request, s, stove, { name: 'Ramonage', lastMaintenance: monthsAgo(24) }); // overdue
    await createType(request, s, boiler, { name: 'Entretien annuel', lastMaintenance: { kind: 'Unknown' } }); // due in 30 d
    await createType(request, s, boiler, { name: 'Contrôle pression', periodicity: 'Semestrial', lastMaintenance: monthsAgo(1) }); // up to date
    await openAs(page, s, '/fr/dashboard');

    await expect(page.getByTestId('dashboard-greeting')).toHaveText('Bonjour Maint');
    await expect(page.getByTestId('dashboard-title')).toHaveText('2 entretiens à traiter');
    await expect(page.getByTestId('dashboard-subtitle')).toHaveText('dont 1 en retard');
    await expect(page.getByTestId('progress-ring')).toHaveAttribute('aria-label', '1/3 à jour');

    const overdue = page.getByTestId('group-overdue');
    await expect(overdue.getByText('En retard', { exact: true })).toBeVisible();
    await expect(overdue.getByTestId('maintenance-row')).toHaveCount(1);
    await expect(overdue.getByTestId('maintenance-row-subtitle')).toHaveText('Poêle à bois · Maison des Lilas');
    const due = page.getByTestId('group-due');
    await expect(due.getByText('À faire prochainement')).toBeVisible();
    await expect(due.getByTestId('maintenance-row')).toHaveCount(1);
    await expect(page.getByText('Contrôle pression')).toHaveCount(0);

    // P07 menu: « Fait à une autre date… » only.
    await overdue.getByTestId('maintenance-row-menu').click();
    await expect(page.getByTestId('menu-done-other-date')).toBeVisible();
    await expect(page.getByTestId('menu-edit-type')).toHaveCount(0);
    await expect(page.getByTestId('menu-delete-type')).toHaveCount(0);
    await page.getByTestId('menu-done-other-date').focus();
    await page.keyboard.press('Escape'); // close the menu

    // « Mes maisons »: C4 cards + « Tout voir » → P08; score card « sur 1 maison ».
    await expect(page.getByTestId('dashboard-houses').getByTestId('house-card')).toHaveCount(1);
    await expect(page.getByTestId('dashboard-houses-all')).toHaveAttribute('href', '/fr/houses');
    await expect(page.getByTestId('dashboard-score-houses')).toHaveText('sur 1 maison');
  });

  test("C'est fait removes the row and updates the counters and the nav badge", async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s);
    const stove = await createDevice(request, s, houseId, { name: 'Poêle à bois', type: 'Poêle à Bois' });
    await createType(request, s, stove, { name: 'Ramonage', lastMaintenance: monthsAgo(24) });
    await createType(request, s, stove, { name: 'Nettoyage vitre', lastMaintenance: { kind: 'Unknown' } });
    await openAs(page, s, '/fr/dashboard');

    await expect(page.getByTestId('dashboard-title')).toContainText('2 entretiens à traiter');
    const badge = page.locator('[data-testid="nav-badge"]:visible').first();
    await expect(badge).toHaveText(/^2/);
    const row = page.getByTestId('maintenance-row').filter({ hasText: 'Ramonage' });
    await row.getByTestId('mark-done').click();

    await expect(page.getByTestId('toast-recorded')).toContainText('Ramonage enregistré');
    await expect(page.getByTestId('toast-recorded')).toContainText('Prochain :');
    await expect(row).toHaveCount(0);
    await expect(page.getByTestId('dashboard-title')).toContainText('1 entretien à traiter');
    await expect(page.getByTestId('dashboard-subtitle')).toHaveCount(0);
    await expect(page.getByTestId('progress-ring')).toHaveAttribute('aria-label', '1/2 à jour');
    await expect(badge).toHaveText(/^1/);
  });

  test('Everything up to date → « Tout est à jour · Prochain : … »', async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s);
    const boiler = await createDevice(request, s, houseId, { name: 'Chaudière gaz', type: 'Chaudière Gaz' });
    await createType(request, s, boiler, { name: 'Contrôle pression', lastMaintenance: monthsAgo(2) });
    await openAs(page, s, '/fr/dashboard');

    await expect(page.getByTestId('dashboard-title')).toHaveText('Tout est à jour');
    await expect(page.getByTestId('dashboard-subtitle')).toContainText('Prochain : Contrôle pression · ');
    await expect(page.getByTestId('progress-ring')).toHaveAttribute('aria-label', '1/1 à jour');
    await expect(page.getByTestId('dashboard-tasks')).toHaveCount(0);
  });

  test('Row click opens the device page (P10)', async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s);
    const stove = await createDevice(request, s, houseId, { name: 'Poêle à bois', type: 'Poêle à Bois' });
    await createType(request, s, stove, { name: 'Ramonage', lastMaintenance: monthsAgo(24) });
    await openAs(page, s, '/fr/dashboard');

    await page.getByTestId('maintenance-row').filter({ hasText: 'Ramonage' }).getByTestId('maintenance-row-subtitle').click();
    await expect(page).toHaveURL(new RegExp(`/fr/devices/${stove}$`));
  });
});
