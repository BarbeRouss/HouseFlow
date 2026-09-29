import { test, expect } from '@playwright/test';
import { createInvitationViaApi, registerViaApi } from '../fixtures/auth';
import {
  createDevice, createHouse, createType, isoDaysAgo, logRecord, monthsAgo, openAs, registerUser,
} from '../fixtures/maintenance-seed';

/** P10 · device page: header, breadcrumb, sections, R5 (hidden actions), deletion, P13. */
test.describe('Device page (P10)', () => {
  test('Header, breadcrumb and sections — no score percentage', async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s, 'Maison des Lilas');
    const deviceId = await createDevice(request, s, houseId, {
      name: 'Chaudière gaz', type: 'Chaudière Gaz', brand: 'Viessmann', model: 'Vitodens 200', installDate: '2019-05-01',
    });
    const typeId = await createType(request, s, deviceId, { name: 'Entretien annuel', lastMaintenance: monthsAgo(11) });
    await createType(request, s, deviceId, { name: 'Contrôle pression', periodicity: 'Semestrial', lastMaintenance: monthsAgo(1) });
    await logRecord(request, s, typeId, { date: isoDaysAgo(30), provider: 'Chauffage Martin', cost: 120 });
    await openAs(page, s, `/fr/devices/${deviceId}`);

    await expect(page.getByRole('heading', { level: 1, name: 'Chaudière gaz' })).toBeVisible();
    await expect(page.getByTestId('device-subtitle')).toHaveText('Viessmann Vitodens 200 · installée en 2019');
    const breadcrumb = page.getByTestId('breadcrumb');
    await expect(breadcrumb.getByRole('link')).toHaveText(['Maisons', 'Maison des Lilas']);
    await expect(breadcrumb.locator('[aria-current="page"]')).toHaveText('Chaudière gaz');
    await expect(page.getByRole('heading', { level: 2, name: 'Entretiens' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Historique' })).toBeVisible();

    // C3 rows sorted by due date, subtitle = periodicity in words.
    const rows = page.getByTestId('maintenance-row');
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0).getByTestId('maintenance-row-subtitle')).toHaveText('Tous les 6 mois');
    await expect(rows.nth(1).getByTestId('maintenance-row-subtitle')).toHaveText('Tous les ans');

    await expect(page.getByTestId('history-total')).toHaveText('Total : 120 €');
    // History table (≥ 640 px): Date · Entretien · Prestataire · Coût.
    await expect(page.getByTestId('history')).toContainText('Prestataire');
    await expect(page.getByTestId('history-row').first()).toContainText('Chauffage Martin');
    await expect(page.getByTestId('history-row').first()).toContainText('120 €');
    await expect(page.getByText(/\d+\s?%/)).toHaveCount(0);
    await expect(page.getByText(/statistiques/i)).toHaveCount(0);
  });

  test('Breadcrumb « Maisons › {maison} › {appareil} », « ‹ {maison} » under 640 px', async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s, 'Chalet');
    const deviceId = await createDevice(request, s, houseId, { name: 'Poêle', type: 'Poêle à Bois' });
    await openAs(page, s, `/fr/devices/${deviceId}`);

    const breadcrumb = page.getByTestId('breadcrumb');
    await breadcrumb.getByRole('link', { name: 'Chalet' }).click();
    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseId}$`));

    // In-app Back (no reload: the seeded session lives in memory).
    await page.goBack();
    await expect(page).toHaveURL(new RegExp(`/fr/devices/${deviceId}$`));
    await breadcrumb.getByRole('link', { name: 'Maisons' }).click();
    await expect(page).toHaveURL(/\/fr\/houses$/);

    // Mobile: a single back link to the parent house.
    await page.goBack();
    await page.setViewportSize({ width: 390, height: 844 });
    const back = page.getByTestId('breadcrumb-back');
    await expect(back).toHaveText('Chalet');
    await expect(breadcrumb.getByRole('link', { name: 'Maisons' })).toHaveCount(0);
    await back.click();
    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseId}$`));
  });

  test('Empty states: no maintenance, no history', async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s);
    const deviceId = await createDevice(request, s, houseId, { name: 'Toiture' });
    await openAs(page, s, `/fr/devices/${deviceId}`);

    await expect(page.getByTestId('types-empty')).toContainText('Aucun entretien');
    await expect(page.getByTestId('types-empty').getByTestId('add-maintenance-type')).toBeVisible();
    await expect(page.getByTestId('history-empty')).toHaveText("Aucun entretien enregistré pour l'instant.");

    // M6 for a device without any maintenance: no « Son entretien… » (0 would read as singular in fr).
    await page.getByTestId('device-menu').click();
    await page.getByTestId('device-delete').click();
    const dialog = page.getByTestId('device-delete-dialog');
    await expect(dialog).toContainText('Cet appareil sera supprimé définitivement.');
    await expect(dialog).not.toContainText('Son entretien');
  });

  test('Delete the device (M6) → house page with the toast', async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s);
    const deviceId = await createDevice(request, s, houseId, { name: 'VMC salle de bain', type: 'VMC' });
    await createType(request, s, deviceId, { name: 'Nettoyage des bouches', periodicity: 'Semestrial' });
    await openAs(page, s, `/fr/devices/${deviceId}`);

    await page.getByTestId('device-menu').click();
    await page.getByTestId('device-delete').click();
    const dialog = page.getByTestId('device-delete-dialog');
    await expect(dialog.getByRole('heading', { name: 'Supprimer VMC salle de bain ?' })).toBeVisible();
    await expect(dialog).toContainText('Son entretien et son historique seront supprimés définitivement.');
    await dialog.getByTestId('confirm-action').click();

    await expect(page).toHaveURL(new RegExp(`/fr/houses/${houseId}$`));
    await expect(page.getByTestId('toast-deleted')).toHaveText(/VMC salle de bain supprimé/);
  });

  test('Read-only member: no « C\'est fait », no menus, no add button, history not clickable', async ({ page, request }) => {
    const owner = await registerUser(request, 'Owner', 'Lilas');
    const houseId = await createHouse(request, owner);
    const deviceId = await createDevice(request, owner, houseId, { name: 'Chaudière gaz', type: 'Chaudière Gaz' });
    const typeId = await createType(request, owner, deviceId, { name: 'Entretien annuel', lastMaintenance: monthsAgo(24) });
    await logRecord(request, owner, typeId, { date: isoDaysAgo(400), provider: 'Chauffage Martin' });

    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'CollaboratorRO');
    const ro = await registerViaApi(request, { firstName: 'Read', lastName: 'Only', email: invitation.email }, invitation.token);
    await openAs(page, { token: ro.token, refreshCookie: ro.refreshCookie, userId: ro.userId }, `/fr/devices/${deviceId}`);

    const r = page.getByTestId('maintenance-row');
    await expect(r).toHaveCount(1);
    await expect(r.getByTestId('mark-done')).toHaveCount(0);
    await expect(r.getByTestId('maintenance-row-menu')).toHaveCount(0);
    await expect(page.getByTestId('device-menu')).toHaveCount(0);
    await expect(page.getByTestId('add-maintenance-type')).toHaveCount(0);
    await expect(page.getByTestId('history').getByRole('button')).toHaveCount(0);
  });

  test('Tenant: « C\'est fait » and « Fait à une autre date… » only', async ({ page, request }) => {
    const owner = await registerUser(request, 'Owner', 'Studio');
    const houseId = await createHouse(request, owner, 'Studio');
    const deviceId = await createDevice(request, owner, houseId, { name: 'Chauffe-eau', type: 'Chauffe-eau' });
    await createType(request, owner, deviceId, { name: 'Détartrage', periodicity: 'Biennial', lastMaintenance: monthsAgo(30) });

    const invitation = await createInvitationViaApi(request, owner.token, houseId, 'Tenant');
    const tenant = await registerViaApi(request, { firstName: 'Ten', lastName: 'Ant', email: invitation.email }, invitation.token);
    await openAs(page, { token: tenant.token, refreshCookie: tenant.refreshCookie, userId: tenant.userId }, `/fr/devices/${deviceId}`);

    const r = page.getByTestId('maintenance-row');
    await expect(r.getByTestId('mark-done')).toBeVisible();
    await r.getByTestId('maintenance-row-menu').click();
    await expect(page.getByTestId('menu-done-other-date')).toBeVisible();
    await expect(page.getByTestId('menu-edit-type')).toHaveCount(0);
    await expect(page.getByTestId('menu-delete-type')).toHaveCount(0);
    // Opened with the mouse, the menu has focused its first item: Escape closes it.
    await expect(page.getByTestId('menu-done-other-date')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('menu-done-other-date')).toHaveCount(0);
    await expect(r.getByTestId('maintenance-row-menu')).toBeFocused();
    await expect(page.getByTestId('device-menu')).toHaveCount(0);
    await expect(page.getByTestId('add-maintenance-type')).toHaveCount(0);

    // A tenant can't delete records: no « Annuler » on the toast.
    await r.getByTestId('mark-done').click();
    const toast = page.getByTestId('toast-recorded');
    await expect(toast).toBeVisible();
    await expect(toast.getByRole('button', { name: 'Annuler' })).toHaveCount(0);
  });

  test('Unknown device → P13 not found', async ({ page, request }) => {
    const s = await registerUser(request);
    await openAs(page, s, '/fr/devices/00000000-0000-0000-0000-000000000000');
    await expect(page.getByTestId('error-page')).toHaveAttribute('data-code', '404');
  });
});
