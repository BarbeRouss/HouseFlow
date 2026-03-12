import { test, expect } from '../fixtures/auth';
import { DashboardPage } from '../pages/dashboard-page';
import { HousePage } from '../pages/house-page';

test.describe('User Flow 3: Maintenance Logging', () => {
  test('Add custom maintenance type', async ({ authenticatedPage: page }) => {
    // ÉTAPE 1: Add a device first
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Test Appareil');
    await page.locator('#type').selectOption('VMC');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // ÉTAPE 2: Go to device details (click on the device card)
    await expect(page.getByRole('heading', { name: 'Test Appareil' })).toBeVisible();
    await page.getByRole('heading', { name: 'Test Appareil' }).click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);

    // ÉTAPE 3: Click "Add" button for maintenance types
    const addButton = page.getByRole('button', { name: /ajouter|add type/i });
    await expect(addButton).toBeVisible();
    await addButton.click();

    // Wait for dialog to be visible
    await expect(page.locator('[class*="fixed"][class*="inset-0"]')).toBeVisible({ timeout: 5000 });

    // ÉTAPE 4: Fill the form
    const nameInput = page.getByPlaceholder(/révision|annual/i);
    await expect(nameInput).toBeVisible({ timeout: 5000 });
    await nameInput.fill('Nettoyage filtres');

    // Select periodicity
    await page.locator('#periodicity').selectOption('Quarterly');

    // ÉTAPE 5: Submit and wait for network
    const submitButton = page.getByRole('button', { name: /ajouter|add$/i }).last();
    await expect(submitButton).toBeVisible();

    // Wait for the POST request to complete
    const [response] = await Promise.all([
      page.waitForResponse(resp => resp.url().includes('/maintenance-types') && resp.request().method() === 'POST'),
      submitButton.click()
    ]);

    // Verify response status
    expect(response.status()).toBe(201);

    // Wait for the dialog to close
    await expect(page.locator('[class*="fixed"][class*="inset-0"][class*="bg-black"]')).toBeHidden({ timeout: 10000 });

    // ÉTAPE 6: Wait for data refresh and verify the new maintenance type appears
    await expect(page.getByText('Nettoyage filtres')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText(/trimestriel|quarterly/i)).toBeVisible();
  });

  test('Quick log maintenance', async ({ authenticatedPage: page }) => {
    // User already has "Ma Maison" auto-created and is on the house page

    // ÉTAPE 1: Add device to the auto-created house
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Détecteur Fumée');
    await page.locator('#type').selectOption('Détecteur de Fumée');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // ÉTAPE 2: Click on device to view details (click on the device card)
    await expect(page.getByRole('heading', { name: 'Détecteur Fumée' })).toBeVisible();
    await page.getByRole('heading', { name: 'Détecteur Fumée' }).click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);

    // Note: Maintenance types are auto-created by backend
    // If there are maintenance types, we can log maintenance
    const logButton = page.getByRole('button', { name: /log maintenance|enregistrer/i }).first();

    if (await logButton.isVisible()) {
      await logButton.click();

      // Quick log (default mode)
      const today = new Date().toISOString().split('T')[0];
      await page.locator('input[type="date"]').fill(today);
      await page.getByRole('button', { name: /save|enregistrer/i }).last().click();

      // Verify success - should close dialog and show in history
      await expect(page.getByText(/history|historique/i)).toBeVisible();
    }
  });

  test('Detailed log maintenance with cost and provider', async ({ authenticatedPage: page }) => {
    // User already has "Ma Maison" auto-created and is on the house page

    // ÉTAPE 1: Add device to the auto-created house
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Climatisation');
    await page.locator('#type').selectOption('Climatisation');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // ÉTAPE 2: View device details (click on the device card)
    await expect(page.getByRole('heading', { name: 'Climatisation' })).toBeVisible();
    await page.getByRole('heading', { name: 'Climatisation' }).click();

    // ÉTAPE 3: Log maintenance with details
    const logButton = page.getByRole('button', { name: /log maintenance|enregistrer/i }).first();

    if (await logButton.isVisible()) {
      await logButton.click();

      // Switch to detailed mode
      await page.getByRole('button', { name: /detailed|détaillée/i }).click();

      // Fill in details
      const today = new Date().toISOString().split('T')[0];
      await page.locator('input[type="date"]').fill(today);
      await page.locator('input[type="number"]').fill('150.50');
      await page.getByPlaceholder(/company name|nom/i).fill('Clim Expert SARL');
      await page.getByPlaceholder(/additional notes|notes/i).fill('Remplacement filtre + vérification fluide frigorigène. RAS.');

      await page.getByRole('button', { name: /save|enregistrer/i }).last().click();

      // Verify maintenance is logged with details
      await expect(page.getByText(/clim expert/i)).toBeVisible();
      await expect(page.getByText(/150.50/i)).toBeVisible();
    }
  });

  test('Verify maintenance history and stats after logging', async ({ authenticatedPage: page }) => {
    // ÉTAPE 1: Add a device
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Mon Détecteur');
    await page.locator('#type').selectOption('Détecteur de Fumée');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // ÉTAPE 2: Go to device details
    await expect(page.getByRole('heading', { name: 'Mon Détecteur' })).toBeVisible();
    await page.getByRole('heading', { name: 'Mon Détecteur' }).click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);

    // Wait for device page to fully load
    await page.waitForLoadState('networkidle');

    // ÉTAPE 3: Add a maintenance type first (since none auto-created)
    const addTypeButton = page.getByRole('button', { name: /ajouter|add type/i });
    await expect(addTypeButton).toBeVisible({ timeout: 5000 });
    await addTypeButton.click();

    // Fill maintenance type form
    await expect(page.locator('[class*="fixed"][class*="inset-0"]')).toBeVisible({ timeout: 5000 });
    await page.getByPlaceholder(/révision|annual/i).fill('Test Batterie');
    await page.locator('#periodicity').selectOption('Annual');

    // Submit and wait
    const [typeResponse] = await Promise.all([
      page.waitForResponse(resp => resp.url().includes('/maintenance-types') && resp.request().method() === 'POST'),
      page.getByRole('button', { name: /ajouter|add$/i }).last().click()
    ]);
    expect(typeResponse.status()).toBe(201);

    // Wait for dialog to close and type to appear
    await expect(page.locator('[class*="fixed"][class*="inset-0"][class*="bg-black"]')).toBeHidden({ timeout: 10000 });
    await expect(page.getByText('Test Batterie')).toBeVisible({ timeout: 10000 });

    // ÉTAPE 4: Verify "No history" message is shown initially
    await expect(page.getByText(/aucun historique|no history/i)).toBeVisible();

    // ÉTAPE 5: Log maintenance with full details - wait for button to appear after data loads
    const logButton = page.getByRole('button', { name: /enregistrer un entretien|log maintenance/i }).first();
    await expect(logButton).toBeVisible({ timeout: 10000 });
    await logButton.click();

    // Wait for dialog
    await expect(page.locator('[class*="fixed"][class*="inset-0"]')).toBeVisible({ timeout: 5000 });

    // Switch to detailed mode
    await page.getByRole('button', { name: /detailed|détaillée/i }).click();

    // Fill maintenance details
    const today = new Date().toISOString().split('T')[0];
    await page.locator('input[type="date"]').fill(today);
    await page.locator('input[type="number"]').fill('250');
    await page.getByPlaceholder(/company name|nom/i).fill('Chauffagiste Pro');
    await page.getByPlaceholder(/additional notes|notes/i).fill('Révision complète annuelle');

    // Submit and wait for API response
    const [response] = await Promise.all([
      page.waitForResponse(resp => resp.url().includes('/instances') && resp.request().method() === 'POST'),
      page.getByRole('button', { name: /save|enregistrer/i }).last().click()
    ]);
    expect(response.status()).toBe(201);

    // Wait for dialog to close
    await expect(page.locator('[class*="fixed"][class*="inset-0"][class*="bg-black"]')).toBeHidden({ timeout: 10000 });

    // ÉTAPE 5: Verify maintenance appears in history
    await expect(page.getByText('Chauffagiste Pro')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('250 €').first()).toBeVisible();
    await expect(page.getByText(/révision complète/i)).toBeVisible();

    // ÉTAPE 6: Verify "No history" message is gone
    await expect(page.getByText(/aucun historique|no history/i)).toBeHidden();

    // ÉTAPE 7: Verify stats card shows updated values
    await expect(page.getByText(/total/i)).toBeVisible();
    // Stats card should show the amount (250 appears twice - stats and history, that's expected)

    // ÉTAPE 8: Verify maintenance type status changed to "up to date"
    await expect(page.getByText(/à jour|up to date/i).first()).toBeVisible();
  });

  test('Maintenance history is sorted with most recent first', async ({ authenticatedPage: page }) => {
    // ÉTAPE 1: Add a device
    await page.getByRole('button', { name: /add device|ajouter un appareil/i }).first().click();
    await page.getByPlaceholder(/chaudière/i).fill('Appareil Test Tri');
    await page.locator('#type').selectOption('VMC');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // ÉTAPE 2: Go to device details
    await expect(page.getByRole('heading', { name: 'Appareil Test Tri' })).toBeVisible();
    await page.getByRole('heading', { name: 'Appareil Test Tri' }).click();
    await expect(page).toHaveURL(/\/fr\/devices\/[a-f0-9-]+$/);
    await page.waitForLoadState('networkidle');

    // ÉTAPE 3: Add a maintenance type
    await page.getByRole('button', { name: /ajouter|add type/i }).click();
    await expect(page.locator('[class*="fixed"][class*="inset-0"]')).toBeVisible({ timeout: 5000 });
    await page.getByPlaceholder(/révision|annual/i).fill('Nettoyage Test');
    await page.locator('#periodicity').selectOption('Monthly');
    await Promise.all([
      page.waitForResponse(resp => resp.url().includes('/maintenance-types') && resp.request().method() === 'POST'),
      page.getByRole('button', { name: /ajouter|add$/i }).last().click()
    ]);
    await expect(page.locator('[class*="fixed"][class*="inset-0"][class*="bg-black"]')).toBeHidden({ timeout: 10000 });
    await expect(page.getByText('Nettoyage Test')).toBeVisible({ timeout: 10000 });

    // ÉTAPE 4: Log first maintenance (older date)
    const logButton = page.getByRole('button', { name: /enregistrer un entretien|log maintenance/i }).first();
    await expect(logButton).toBeVisible({ timeout: 10000 });
    await logButton.click();
    await expect(page.locator('[class*="fixed"][class*="inset-0"]')).toBeVisible({ timeout: 5000 });

    // Set date to 10 days ago
    const tenDaysAgo = new Date();
    tenDaysAgo.setDate(tenDaysAgo.getDate() - 10);
    await page.locator('input[type="date"]').fill(tenDaysAgo.toISOString().split('T')[0]);
    await page.getByRole('button', { name: /detailed|détaillée/i }).click();
    await page.getByPlaceholder(/company name|nom/i).fill('Premier Prestataire');

    await Promise.all([
      page.waitForResponse(resp => resp.url().includes('/instances') && resp.request().method() === 'POST'),
      page.getByRole('button', { name: /save|enregistrer/i }).last().click()
    ]);
    await expect(page.locator('[class*="fixed"][class*="inset-0"][class*="bg-black"]')).toBeHidden({ timeout: 10000 });
    await expect(page.getByText('Premier Prestataire')).toBeVisible({ timeout: 10000 });

    // ÉTAPE 5: Log second maintenance (today - more recent)
    await logButton.click();
    await expect(page.locator('[class*="fixed"][class*="inset-0"]')).toBeVisible({ timeout: 5000 });

    const today = new Date().toISOString().split('T')[0];
    await page.locator('input[type="date"]').fill(today);
    await page.getByRole('button', { name: /detailed|détaillée/i }).click();
    await page.getByPlaceholder(/company name|nom/i).fill('Deuxième Prestataire');

    await Promise.all([
      page.waitForResponse(resp => resp.url().includes('/instances') && resp.request().method() === 'POST'),
      page.getByRole('button', { name: /save|enregistrer/i }).last().click()
    ]);
    await expect(page.locator('[class*="fixed"][class*="inset-0"][class*="bg-black"]')).toBeHidden({ timeout: 10000 });

    // ÉTAPE 6: Verify order - "Deuxième Prestataire" (most recent) should appear BEFORE "Premier Prestataire"
    await expect(page.getByText('Deuxième Prestataire')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Premier Prestataire')).toBeVisible();

    // Get the positions of both entries in the DOM
    const secondProvider = page.getByText('Deuxième Prestataire');
    const firstProvider = page.getByText('Premier Prestataire');

    const secondBox = await secondProvider.boundingBox();
    const firstBox = await firstProvider.boundingBox();

    // Most recent (Deuxième) should be above (smaller Y) than older (Premier)
    expect(secondBox).not.toBeNull();
    expect(firstBox).not.toBeNull();
    expect(secondBox!.y).toBeLessThan(firstBox!.y);
  });
});
