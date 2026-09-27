import { test, expect } from '../fixtures/auth';
import { HousePage } from '../pages/house-page';

// P08 (houses list), P09 (house page), M1 (create / edit), M6 (delete). The fixture lands on the
// empty P09 of « Ma maison ».
test.describe('User Flow: House Management', () => {
  test('Create a house from the houses list (M1) and land on its page', async ({ authenticatedPage: page }) => {
    const houses = new HousePage(page);

    await houses.gotoList();
    await expect(houses.houseRow('Ma maison')).toBeVisible();

    // M1: « Créer » stays disabled while the name is empty.
    await page.getByTestId('add-house').click();
    await expect(houses.houseModal.getByRole('heading', { name: /nouvelle maison/i })).toBeVisible();
    await expect(houses.houseModal.getByTestId('house-modal-submit')).toBeDisabled();
    await houses.houseModal.getByLabel(/^nom/i).fill('Maison de Vacances');
    await houses.houseModal.getByLabel(/adresse/i).fill('123 Rue de la Plage, Cannes');
    await houses.houseModal.getByTestId('house-modal-submit').click();

    // → P09 of the new house, empty state with « Ajouter un appareil ».
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);
    await expect(page.getByRole('heading', { name: 'Maison de Vacances', level: 1 })).toBeVisible();
    await expect(page.getByTestId('house-address')).toHaveText('123 Rue de la Plage, Cannes');
    await expect(page.getByTestId('devices-empty')).toContainText(/aucun appareil/i);
    await expect(page.getByTestId('add-device')).toBeVisible();
  });

  test('The former creation page is gone', async ({ authenticatedPage: page }) => {
    await page.goto('/fr/houses');
    await expect(page.getByTestId('houses-page')).toBeVisible();
    // No link anywhere to /houses/new.
    await expect(page.locator('a[href$="/houses/new"]')).toHaveCount(0);
  });

  test('Navigate between houses: rows, breadcrumb and house selector', async ({ authenticatedPage: page }) => {
    const houses = new HousePage(page);
    await houses.createHouse('Appartement Paris');

    // P08 lists both houses as C4 rows (no percentage).
    await houses.gotoList();
    await expect(houses.houseRows()).toHaveCount(2);
    await expect(page.getByText(/%/)).toHaveCount(0);
    await expect(houses.houseRow('Ma maison').getByTestId('house-row-subtitle')).toHaveText(/0 appareil/);

    // The whole row is clickable (link stretched over the row).
    await houses.houseRow('Ma maison').click({ position: { x: 5, y: 5 } });
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);
    await expect(page.getByRole('heading', { name: 'Ma maison', level: 1 })).toBeVisible();

    // Breadcrumb selector → the other house.
    await page.getByTestId('house-selector').click();
    await page.getByTestId('house-selector-item').filter({ hasText: 'Appartement Paris' }).click();
    await expect(page.getByRole('heading', { name: 'Appartement Paris', level: 1 })).toBeVisible();

    // « Maisons » → P08.
    await page.getByTestId('breadcrumb-houses').click();
    await expect(page).toHaveURL(/\/fr\/houses$/);
    await expect(page.getByRole('heading', { name: 'Maisons', level: 1 })).toBeVisible();
  });

  test('Edit the house from the ⋯ menu (M1 edit mode)', async ({ authenticatedPage: page }) => {
    const houses = new HousePage(page);

    await houses.openHouseMenu();
    await page.getByTestId('house-menu-edit').click();
    await expect(houses.houseModal.getByRole('heading', { name: /modifier la maison/i })).toBeVisible();
    await expect(houses.houseModal.getByTestId('house-name')).toHaveValue('Ma maison');

    await houses.houseModal.getByTestId('house-name').fill('Maison du Lac');
    await houses.houseModal.getByTestId('house-address').fill('1 chemin du Lac, Annecy');
    await houses.houseModal.getByTestId('house-modal-submit').click();

    await expect(houses.houseModal).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Maison du Lac', level: 1 })).toBeVisible();
    await expect(page.getByTestId('house-address')).toHaveText('1 chemin du Lac, Annecy');
    await expect(page.getByTestId('toast')).toHaveText(/modifications enregistrées/i);
  });

  test('Delete the house (M6) → houses list + toast', async ({ authenticatedPage: page }) => {
    const houses = new HousePage(page);
    await houses.createHouse('Maison à supprimer');

    await houses.openHouseMenu();
    await page.getByTestId('house-menu-delete').click();
    const dialog = page.getByTestId('delete-house-dialog');
    await expect(dialog.getByRole('heading', { name: 'Supprimer Maison à supprimer ?' })).toBeVisible();
    await expect(dialog).toContainText(/supprimée définitivement/i);
    await dialog.getByTestId('confirm-action').click();

    await expect(page).toHaveURL(/\/fr\/houses$/);
    await expect(page.getByTestId('toast')).toHaveText('Maison à supprimer supprimé');
    await expect(houses.houseRow('Maison à supprimer')).toHaveCount(0);
    await expect(houses.houseRow('Ma maison')).toBeVisible();
  });
});
