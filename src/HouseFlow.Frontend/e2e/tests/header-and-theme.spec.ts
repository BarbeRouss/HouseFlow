import { test, expect } from '../fixtures/auth';

test.describe('Header and Theme Toggle', () => {
  test('Header is visible on dashboard pages', async ({ authenticatedPage: page }) => {
    // User starts on house page after auth fixture
    // Header should be visible with HouseFlow logo
    const header = page.locator('header');
    await expect(header).toBeVisible();

    // HouseFlow logo/link should be present
    await expect(page.getByRole('link', { name: /houseflow/i })).toBeVisible();
  });

  test('Header shows user initials', async ({ authenticatedPage: page }) => {
    // The auth fixture creates a user with firstName: "Test", lastName: "User"
    // The header should show "TU" initials
    await expect(page.getByText('TU')).toBeVisible();
  });

  test('User dropdown menu shows full name and logout', async ({ authenticatedPage: page }) => {
    // Click on user button (has initials "TU")
    await page.getByText('TU').click();

    // Dropdown should show full name
    await expect(page.getByText('Test User')).toBeVisible();

    // Logout option should be visible
    await expect(page.getByText(/se déconnecter|logout/i)).toBeVisible();
  });

  test('Theme toggle opens dropdown with light/dark/system options', async ({ authenticatedPage: page }) => {
    // Click theme toggle button (has sr-only text "Changer le thème")
    const themeButton = page.getByRole('button', { name: /changer le thème|toggle theme/i });
    await expect(themeButton).toBeVisible();
    await themeButton.click();

    // Verify all three theme options appear
    await expect(page.getByText(/clair|light/i)).toBeVisible();
    await expect(page.getByText(/sombre|dark/i)).toBeVisible();
    await expect(page.getByText(/système|system/i)).toBeVisible();
  });

  test('Switching to dark theme adds dark class to html', async ({ authenticatedPage: page }) => {
    // Open theme dropdown
    const themeButton = page.getByRole('button', { name: /changer le thème|toggle theme/i });
    await themeButton.click();

    // Click "Sombre" (dark)
    await page.getByText(/sombre|dark/i).click();

    // Wait for theme to be applied
    await page.waitForTimeout(500);

    // Verify the html element has the 'dark' class
    const htmlClass = await page.locator('html').getAttribute('class');
    expect(htmlClass).toContain('dark');
  });

  test('Switching back to light theme removes dark class', async ({ authenticatedPage: page }) => {
    // First switch to dark
    const themeButton = page.getByRole('button', { name: /changer le thème|toggle theme/i });
    await themeButton.click();
    await page.getByText(/sombre|dark/i).click();
    await page.waitForTimeout(500);

    // Then switch to light
    await themeButton.click();
    await page.getByText(/clair|light/i).click();
    await page.waitForTimeout(500);

    // Verify the html element has 'light' class (or no 'dark')
    const htmlClass = await page.locator('html').getAttribute('class');
    expect(htmlClass).not.toContain('dark');
  });

  test('HouseFlow logo navigates to dashboard', async ({ authenticatedPage: page }) => {
    // First create a second house so dashboard doesn't auto-redirect
    await page.goto('http://localhost:3000/fr/houses/new');
    await page.getByLabel(/nom|name/i).fill('Maison Test Header');
    await page.getByLabel(/adresse|address/i).fill('1 Rue Test');
    await page.getByLabel(/code postal|zip/i).fill('75001');
    await page.getByLabel(/ville|city/i).fill('Paris');
    await page.getByRole('button', { name: /save|enregistrer/i }).click();
    await expect(page).toHaveURL(/\/fr\/houses\/[a-f0-9-]+$/);

    // Click HouseFlow logo
    await page.getByRole('link', { name: /houseflow/i }).click();

    // Should navigate to dashboard
    await expect(page).toHaveURL(/\/fr\/dashboard/);
  });

  test('Logout redirects to login page', async ({ authenticatedPage: page }) => {
    // Click user initials to open dropdown
    await page.getByText('TU').click();

    // Click logout
    await page.getByText(/se déconnecter|logout/i).click();

    // Should redirect to login page
    await expect(page).toHaveURL(/\/fr\/login/, { timeout: 10000 });
  });
});
