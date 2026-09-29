import { test, expect, Page } from '@playwright/test';
import {
  createDevice, createHouse, createType, isoDaysAgo, logRecord, monthsAgo, openAs, registerUser, Session,
} from '../fixtures/maintenance-seed';

/**
 * Maintenance flows on P10 (C3 rows, C5 toasts, M3, M4, M6). Data is seeded through the API; the
 * browser only drives the device page.
 */

const row = (page: Page, name: string) => page.getByTestId('maintenance-row').filter({ hasText: name });

async function seedDevice(request: Parameters<typeof registerUser>[0]) {
  const s = await registerUser(request);
  const houseId = await createHouse(request, s);
  const deviceId = await createDevice(request, s, houseId, { name: 'Chaudière gaz', type: 'Chaudière Gaz' });
  return { s, houseId, deviceId };
}

async function openDevice(page: Page, s: Session, deviceId: string) {
  await openAs(page, s, `/fr/devices/${deviceId}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Chaudière gaz' })).toBeVisible();
}

test.describe('Maintenance on the device page (P10)', () => {
  test("C'est fait records today: row up to date, history line, toast with its actions", async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    // Last done two years ago, yearly → overdue.
    await createType(request, s, deviceId, { name: 'Entretien annuel', lastMaintenance: monthsAgo(24) });
    await openDevice(page, s, deviceId);

    const r = row(page, 'Entretien annuel');
    await expect(r).toHaveAttribute('data-status', 'overdue');
    await expect(r).toContainText(/En retard de \d+ j/);
    await expect(r.getByTestId('maintenance-row-subtitle')).toHaveText('Tous les ans');
    const done = r.getByTestId('mark-done');
    await expect(done).toHaveClass(/hf-btn-primary/); // filled when overdue
    // The month-precision « Dernier entretien » is itself a record (1st of that month).
    await expect(page.getByTestId('history-row')).toHaveCount(1);

    await done.click();

    const toast = page.getByTestId('toast-recorded');
    await expect(toast).toContainText('Entretien annuel enregistré');
    await expect(toast).toContainText(/Prochain : \p{L}+ \d{4}/u); // « Prochain : septembre 2027 »
    await expect(toast.getByRole('button', { name: 'Ajouter des détails' })).toBeVisible();
    await expect(toast.getByRole('button', { name: 'Annuler' })).toBeVisible();
    await expect(r).toHaveAttribute('data-status', 'ok');
    await expect(r.getByTestId('mark-done')).toHaveClass(/hf-btn-outline/);
    await expect(page.getByTestId('history-row')).toHaveCount(2); // newest first
    await expect(page.getByTestId('history-row').first()).toContainText('Entretien annuel');
  });

  test('Toast « Annuler » deletes the record just created', async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    await createType(request, s, deviceId, { name: 'Entretien annuel', lastMaintenance: monthsAgo(24) });
    await openDevice(page, s, deviceId);

    const before = await page.getByTestId('history-row').count();
    await row(page, 'Entretien annuel').getByTestId('mark-done').click();
    await expect(page.getByTestId('history-row')).toHaveCount(before + 1);

    const del = page.waitForResponse(r => r.url().includes('/maintenance-instances/') && r.request().method() === 'DELETE');
    await page.getByTestId('toast-recorded').getByRole('button', { name: 'Annuler' }).click();
    expect((await del).status()).toBe(204);
    await expect(page.getByTestId('history-row')).toHaveCount(before);
    await expect(row(page, 'Entretien annuel')).toHaveAttribute('data-status', 'overdue');
  });

  test('« Ajouter des détails » opens M3 on the new record and saves the details', async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    await createType(request, s, deviceId, { name: 'Entretien annuel', lastMaintenance: monthsAgo(24) });
    await openDevice(page, s, deviceId);

    await row(page, 'Entretien annuel').getByTestId('mark-done').click();
    await page.getByTestId('toast-recorded').getByRole('button', { name: 'Ajouter des détails' }).click();

    const modal = page.getByTestId('record-modal');
    await expect(modal.getByRole('heading', { name: 'Entretien annuel', exact: true })).toBeVisible();
    await expect(modal.getByTestId('record-delete')).toBeVisible(); // edit mode
    await modal.getByTestId('record-provider').fill('Chauffage Martin');
    await modal.getByTestId('record-cost').fill('120,50');
    await modal.getByTestId('record-save').click();

    await expect(modal).toBeHidden();
    await expect(page.getByTestId('toast-changes-saved')).toHaveText(/Modifications enregistrées/);
    const history = page.getByTestId('history-row').first();
    await expect(history).toContainText('Chauffage Martin');
    await expect(history).toContainText('120,5 €');
    await expect(page.getByTestId('history-total')).toHaveText('Total : 120,5 €');
  });

  test('« Fait à une autre date… » (M3 create): date required and not in the future', async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    await createType(request, s, deviceId, { name: 'Ramonage', lastMaintenance: { kind: 'Unknown' } });
    await openDevice(page, s, deviceId);

    await row(page, 'Ramonage').getByTestId('maintenance-row-menu').click();
    await page.getByTestId('menu-done-other-date').click();

    const modal = page.getByTestId('record-modal');
    const save = modal.getByTestId('record-save');
    await expect(modal.getByTestId('record-delete')).toHaveCount(0); // create mode
    await expect(save).toBeDisabled(); // empty date

    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 2);
    await modal.getByTestId('record-date').fill(tomorrow.toISOString().split('T')[0]);
    await expect(modal.getByText('La date ne peut pas être dans le futur.')).toBeVisible();
    await expect(save).toBeDisabled();

    await modal.getByTestId('record-date').fill(isoDaysAgo(10));
    await modal.getByTestId('record-provider').fill('Ramoneur Dupont SARL');
    await modal.getByTestId('record-cost').fill('abc');
    await expect(save).toBeDisabled();
    await modal.getByTestId('record-cost').fill('80');
    await modal.getByTestId('record-note').fill('RAS');

    const post = page.waitForResponse(r => r.url().includes('/instances') && r.request().method() === 'POST');
    await save.click();
    expect((await post).status()).toBe(201);
    await expect(page.getByTestId('toast-recorded')).toContainText('Ramonage enregistré');
    await expect(page.getByTestId('history-row').first()).toContainText('Ramoneur Dupont SARL');
    await expect(row(page, 'Ramonage')).toHaveAttribute('data-status', 'ok');
  });

  test('History row → M3 edit; « Supprimer » without confirmation, then « Annuler » restores it', async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    const typeId = await createType(request, s, deviceId, { name: 'Entretien annuel' });
    await logRecord(request, s, typeId, { date: isoDaysAgo(40), provider: 'Chauffage Martin', cost: 115 });
    await openDevice(page, s, deviceId);

    await page.getByTestId('history-row').filter({ hasText: 'Chauffage Martin' }).click();
    const modal = page.getByTestId('record-modal');
    await expect(modal.getByTestId('record-provider')).toHaveValue('Chauffage Martin');
    await modal.getByTestId('record-delete').click();

    await expect(modal).toBeHidden();
    await expect(page.getByTestId('confirm-dialog')).toHaveCount(0);
    await expect(page.getByTestId('history-empty')).toHaveText('Aucun entretien enregistré pour l\'instant.');
    const toast = page.getByTestId('toast-record-deleted');
    await expect(toast).toContainText('Enregistrement supprimé');

    await toast.getByRole('button', { name: 'Annuler' }).click();
    await expect(page.getByTestId('history-row').filter({ hasText: 'Chauffage Martin' })).toBeVisible();
  });

  test('M3 edit clears provider and cost when the fields are emptied', async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    const typeId = await createType(request, s, deviceId, { name: 'Entretien annuel' });
    await logRecord(request, s, typeId, { date: isoDaysAgo(40), provider: 'Chauffage Martin', cost: 115 });
    await openDevice(page, s, deviceId);
    await expect(page.getByTestId('history-total')).toHaveText('Total : 115 €');

    await page.getByTestId('history-row').filter({ hasText: 'Chauffage Martin' }).click();
    const modal = page.getByTestId('record-modal');
    await modal.getByTestId('record-provider').fill('');
    await modal.getByTestId('record-cost').fill('');
    const put = page.waitForResponse(r => /\/maintenance-instances\/[a-f0-9-]+$/.test(r.url()) && r.request().method() === 'PUT');
    await modal.getByTestId('record-save').click();
    expect((await put).status()).toBe(200);

    await expect(modal).toBeHidden();
    await expect(page.getByTestId('history-row')).toHaveCount(1);
    await expect(page.getByTestId('history-row')).not.toContainText('Chauffage Martin');
    await expect(page.getByTestId('history-total')).toHaveCount(0);
  });

  test('History is sorted newest first, total hidden when 0', async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    const typeId = await createType(request, s, deviceId, { name: 'Entretien annuel' });
    await logRecord(request, s, typeId, { date: isoDaysAgo(400), provider: 'Premier Prestataire' });
    await logRecord(request, s, typeId, { date: isoDaysAgo(20), provider: 'Deuxième Prestataire' });
    await openDevice(page, s, deviceId);

    const rows = page.getByTestId('history-row');
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0)).toContainText('Deuxième Prestataire');
    await expect(rows.nth(1)).toContainText('Premier Prestataire');
    await expect(page.getByTestId('history-total')).toHaveCount(0);
  });

  test('M4: add a maintenance with a custom frequency and a last maintenance month', async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    await createType(request, s, deviceId, { name: 'Entretien annuel' });
    await openDevice(page, s, deviceId);

    await page.getByTestId('add-maintenance-type').click();
    const modal = page.getByTestId('type-modal');
    await expect(modal.getByRole('heading', { name: 'Nouvel entretien' })).toBeVisible();
    const save = modal.getByTestId('type-save');
    await expect(save).toBeDisabled(); // name required
    await expect(modal.getByTestId('type-freq-12')).toHaveAttribute('aria-checked', 'true'); // 1 an by default

    await modal.getByTestId('type-name').fill('Contrôle pression');
    await modal.getByTestId('type-freq-other').click();
    await expect(save).toBeDisabled(); // « Autre » without n
    await modal.getByTestId('type-custom-count').fill('18');

    // « Dernier entretien »: a year without its month blocks the form.
    const year = String(new Date().getFullYear() - 1);
    await modal.getByTestId('type-last-year').selectOption(year);
    await expect(modal.getByTestId('type-last-hint')).toHaveText('Choisissez le mois');
    await expect(save).toBeDisabled();
    await modal.getByTestId('type-last-month').selectOption('3');
    await expect(save).toBeEnabled();

    const post = page.waitForResponse(r => r.url().includes('/maintenance-types') && r.request().method() === 'POST');
    await save.click();
    const body = (await post).request().postDataJSON();
    expect(body).toMatchObject({ name: 'Contrôle pression', periodicity: 'Custom', customMonths: 18,
      lastMaintenance: { kind: 'Month', year: Number(year), month: 3 } });

    await expect(modal).toBeHidden();
    await expect(row(page, 'Contrôle pression').getByTestId('maintenance-row-subtitle')).toHaveText('Tous les 18 mois');
    // The approximate last maintenance is a record on the 1st of that month.
    await expect(page.getByTestId('history-row').filter({ hasText: 'Contrôle pression' })).toHaveCount(1);
  });

  test('M4 edit changes the frequency; M6 deletes the maintenance', async ({ page, request }) => {
    const { s, deviceId } = await seedDevice(request);
    await createType(request, s, deviceId, { name: 'Entretien annuel' });
    await createType(request, s, deviceId, { name: 'Contrôle pression', periodicity: 'Semestrial' });
    await openDevice(page, s, deviceId);

    await row(page, 'Contrôle pression').getByTestId('maintenance-row-menu').click();
    await page.getByTestId('menu-edit-type').click();
    const modal = page.getByTestId('type-modal');
    await expect(modal.getByRole('heading', { name: "Modifier l'entretien" })).toBeVisible();
    await expect(modal.getByTestId('type-freq-6')).toHaveAttribute('aria-checked', 'true');
    await expect(modal.getByTestId('type-last-year')).toHaveCount(0); // creation only
    await modal.getByTestId('type-freq-24').click();
    await modal.getByTestId('type-save').click();
    await expect(page.getByTestId('toast-changes-saved')).toBeVisible();
    await expect(row(page, 'Contrôle pression').getByTestId('maintenance-row-subtitle')).toHaveText('Tous les 2 ans');

    await row(page, 'Contrôle pression').getByTestId('maintenance-row-menu').click();
    await page.getByTestId('menu-delete-type').click();
    const dialog = page.getByTestId('type-delete-dialog');
    await expect(dialog.getByRole('heading', { name: 'Supprimer Contrôle pression ?' })).toBeVisible();
    // No history yet: the zero-record variant, not « Son historique (0 enregistrement)… ».
    await expect(dialog).toContainText('Cet entretien sera supprimé définitivement.');
    await expect(dialog).not.toContainText('enregistrement');
    await expect(dialog.getByRole('button', { name: 'Annuler' })).toBeFocused();
    await dialog.getByTestId('confirm-action').click();

    await expect(page.getByTestId('toast-deleted')).toHaveText(/Contrôle pression supprimé/);
    await expect(row(page, 'Contrôle pression')).toHaveCount(0);
    await expect(row(page, 'Entretien annuel')).toHaveCount(1);
  });

  test('Mobile: status under the subtitle, 44 px button, toast with « Ajouter des détails »', async ({ page, request }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    const { s, deviceId } = await seedDevice(request);
    await createType(request, s, deviceId, { name: 'Entretien annuel', lastMaintenance: monthsAgo(24) });
    await openDevice(page, s, deviceId);

    const r = row(page, 'Entretien annuel');
    // P10: the ⋯ menu stays visible on mobile (specs/ux README §6), 32 × 44 touch target.
    const menu = r.getByTestId('maintenance-row-menu');
    await expect(menu).toBeVisible();
    expect((await menu.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await menu.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('menu-done-other-date')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('menu-done-other-date')).toHaveCount(0);

    const box = await r.getByTestId('mark-done').boundingBox();
    expect(box!.height).toBeGreaterThanOrEqual(44);

    await r.getByTestId('mark-done').click();
    const toast = page.getByTestId('toast-recorded');
    await expect(toast).toContainText('Entretien annuel enregistré');
    // Decision 20 (specs/ux README §6): on mobile « Ajouter des détails » is the path to another date.
    await expect(toast.getByRole('button', { name: 'Ajouter des détails' })).toBeVisible();
    await expect(toast.getByRole('button', { name: 'Annuler' })).toBeVisible();
  });
});
