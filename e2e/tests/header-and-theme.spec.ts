import type { Page } from '@playwright/test';
import { test, expect } from '../fixtures/auth';
import { createDevice, createHouse, createType, monthsAgo, openAs, registerUser } from '../fixtures/maintenance-seed';
import { SettingsPage } from '../pages/settings-page';

test.describe('Header', () => {
  test('Header is visible on dashboard pages', async ({ authenticatedPage: page }) => {
    const header = page.locator('header');
    await expect(header).toBeVisible();

    // HouseFlow logo/link should be present
    await expect(header.getByRole('link', { name: /houseflow/i })).toBeVisible();
  });

  test('Header shows user initials', async ({ authenticatedPage: page }) => {
    // The auth fixture creates a user with firstName: "Test", lastName: "User"
    // The header should show "TU" initials — scope to header to avoid member avatars
    await expect(page.locator('header').getByText('TU')).toBeVisible();
  });

  test('User dropdown menu shows full name and logout', async ({ authenticatedPage: page }) => {
    await page.locator('header').getByText('TU').click();

    await expect(page.getByText('Test User').first()).toBeVisible();
    await expect(page.getByText(/se déconnecter|logout|log out/i)).toBeVisible();
  });

  test('Language and theme are no longer in the header (moved to the account page)', async ({ authenticatedPage: page }) => {
    await expect(page.locator('header').getByRole('button', { name: /changer le thème|toggle theme/i })).toHaveCount(0);
  });

  test('HouseFlow logo navigates to dashboard', async ({ authenticatedPage: page }) => {
    await page.goto('/fr/settings');
    await expect(page.getByTestId('save-profile')).toBeVisible();

    await page.locator('header').getByRole('link', { name: /houseflow/i }).click();

    await expect(page).toHaveURL(/\/fr\/dashboard/);
  });

  test('Logout lands on the landing page (P01)', async ({ authenticatedPage: page }) => {
    await page.locator('header').getByText('TU').click();
    await page.getByText(/se déconnecter|logout|log out/i).click();

    // C1 "Se déconnecter" → P01 (/fr).
    await expect(page).toHaveURL(/\/fr\/?$/, { timeout: 10000 });
    await expect(page.getByTestId('landing-title')).toBeVisible();
    await expect(page.locator('header').getByText('TU')).toHaveCount(0);
  });
});

// P11 · Préférences: the theme and language settings live on the account page now.
test.describe('Account preferences (theme and language)', () => {
  test('Theme control offers light / dark / system', async ({ authenticatedPage: page }) => {
    const settings = new SettingsPage(page);
    await settings.goto();

    await expect(settings.themeOption('system')).toBeVisible();
    await expect(settings.themeOption('light')).toBeVisible();
    await expect(settings.themeOption('dark')).toBeVisible();
    await expect(settings.themeOption('system')).toContainText(/système|system/i);
  });

  test('Switching to dark theme adds the dark class, light removes it, and it persists', async ({ authenticatedPage: page }) => {
    const settings = new SettingsPage(page);
    await settings.goto();

    await settings.themeOption('dark').click();
    await expect(settings.themeOption('dark')).toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('html')).toHaveClass(/(^|\s)dark(\s|$)/);

    await page.reload();
    await expect(page.locator('html')).toHaveClass(/(^|\s)dark(\s|$)/);

    await settings.themeOption('light').click();
    await expect(settings.themeOption('light')).toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('html')).not.toHaveClass(/(^|\s)dark(\s|$)/);
  });

  test('Switching the language changes the URL prefix and the texts', async ({ authenticatedPage: page }) => {
    const settings = new SettingsPage(page);
    await settings.goto();

    await settings.languageOption('en').click();
    await expect(page).toHaveURL(/\/en\/settings/);
    await expect(settings.languageOption('en')).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByRole('heading', { name: 'Preferences' })).toBeVisible();

    await settings.languageOption('fr').click();
    await expect(page).toHaveURL(/\/fr\/settings/);
    await expect(page.getByRole('heading', { name: 'Préférences' })).toBeVisible();
  });
});

// specs/ux « Assets »: the app icon (header logo + favicon) follows the global status — fixed « ok »
// on public pages, « none » without data, « late » as soon as one maintenance is overdue.
test.describe('App icon by global status', () => {
  const favicon = (page: Page) => page.locator('link#hf-favicon');

  test('Public page: fixed « ok » icon', async ({ page }) => {
    await page.goto('/fr/login');
    await expect(favicon(page)).toHaveAttribute('href', 'icons/favicon-ok.svg');
  });

  test('Signed in without maintenance: neutral icon', async ({ authenticatedPage: page }) => {
    await expect(page.locator('header [data-testid="app-icon"]')).toHaveAttribute('data-status', 'none');
    await expect(favicon(page)).toHaveAttribute('href', 'icons/favicon-none.svg');
  });

  test('One overdue maintenance: « late » icon and red badge', async ({ page, request }) => {
    const s = await registerUser(request);
    const houseId = await createHouse(request, s);
    const stove = await createDevice(request, s, houseId, { name: 'Poêle à bois', type: 'Poêle à Bois' });
    await createType(request, s, stove, { name: 'Ramonage', lastMaintenance: monthsAgo(24) });
    await openAs(page, s, '/fr/dashboard');

    await expect(page.locator('header [data-testid="app-icon"]')).toHaveAttribute('data-status', 'late');
    await expect(favicon(page)).toHaveAttribute('href', 'icons/favicon-late.svg');
    await expect(page.locator('[data-testid="nav-badge"]:visible').first()).toHaveAttribute('data-variant', 'late');
  });
});
