import { test, expect } from '../fixtures/auth';

test.describe('User Flow: House Management', () => {
  test('Create a new house from house page', async ({ authenticatedPage: page }) => {
    // User starts on their auto-created house page
    // Navigate directly to create new house page
    await page.goto('http://localhost:3000/fr/houses/new');

    // Fill the house form
    await page.getByLabel(/nom|name/i).fill('Maison de Vacances');
    await page.getByLabel(/adresse|address/i).fill('123 Rue de la Plage');
    await page.getByLabel(/code postal|zip/i).fill('06400');
    await page.getByLabel(/ville|city/i).fill('Cannes');

    // Submit
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // Should be redirected to the new house page
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);
    await expect(page.getByRole('heading', { name: 'Maison de Vacances' })).toBeVisible();
    await expect(page.getByText('Cannes')).toBeVisible();
  });

  test('View house details with score', async ({ authenticatedPage: page }) => {
    // User starts on their auto-created house page "Ma maison"
    // Verify the score is visible (100% for empty house)
    await expect(page.getByText(/100%/).first()).toBeVisible({ timeout: 5000 });

    // Verify house name is displayed
    await expect(page.getByRole('heading', { name: /ma maison/i })).toBeVisible();
  });

  test('Navigate between multiple houses from dashboard', async ({ authenticatedPage: page }) => {
    // First, create a second house (fill all required fields)
    await page.goto('http://localhost:3000/fr/houses/new');
    await page.getByLabel(/nom|name/i).fill('Appartement Paris');
    await page.getByLabel(/adresse|address/i).fill('45 Avenue des Champs');
    await page.getByLabel(/code postal|zip/i).fill('75008');
    await page.getByLabel(/ville|city/i).fill('Paris');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/, { timeout: 10000 });

    // Go to dashboard (with 2 houses, no auto-redirect)
    await page.goto('http://localhost:3000/fr/dashboard');

    // Verify both houses are visible on dashboard
    await expect(page.getByRole('heading', { name: 'Ma maison' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Appartement Paris' })).toBeVisible();

    // Click on "Ma maison" card
    await page.getByRole('heading', { name: 'Ma maison' }).click();
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);
    await expect(page.getByRole('heading', { name: /ma maison/i, level: 1 })).toBeVisible();

    // Navigate back to dashboard via breadcrumb
    await page.getByRole('link', { name: /accueil/i }).click();
    await expect(page).toHaveURL(/\/fr\/dashboard/);

    // Click on "Appartement Paris" card
    await page.getByRole('heading', { name: 'Appartement Paris' }).click();
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);
    await expect(page.getByRole('heading', { name: 'Appartement Paris', level: 1 })).toBeVisible();
  });

  test('Dashboard shows global score with multiple houses', async ({ authenticatedPage: page }) => {
    // First, create a second house so dashboard doesn't auto-redirect (fill all required fields)
    await page.goto('http://localhost:3000/fr/houses/new');
    await page.getByLabel(/nom|name/i).fill('Residence Secondaire');
    await page.getByLabel(/adresse|address/i).fill('10 Rue de la Mer');
    await page.getByLabel(/code postal|zip/i).fill('06000');
    await page.getByLabel(/ville|city/i).fill('Nice');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/, { timeout: 10000 });

    // Navigate to dashboard
    await page.goto('http://localhost:3000/fr/dashboard');

    // Verify welcome message is visible
    await expect(page.getByText(/bienvenue|welcome/i)).toBeVisible({ timeout: 5000 });

    // Verify the score is displayed (100% for empty houses)
    await expect(page.getByText(/100%/).first()).toBeVisible();

    // Verify multiple houses are shown (use headings to be specific)
    await expect(page.getByRole('heading', { name: 'Ma maison' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Residence Secondaire' })).toBeVisible();
  });
});
