import { test, expect } from '@playwright/test';

test.describe('Complete Registration Flow', () => {
  test('User can register and is redirected to device creation', async ({ page }) => {
    // Navigate to register page
    await page.goto('http://localhost:3000/fr/register');

    // Wait for page to load
    await page.waitForLoadState('networkidle');

    // Generate unique email
    const timestamp = Date.now();
    const email = `testuser${timestamp}@houseflow.test`;
    const password = 'TestPassword123!'; // Meets requirements
    const firstName = 'Jean';
    const lastName = 'Dupont';

    // Fill registration form (webkit compatibility - use pressSequentially)
    const firstNameField = page.getByPlaceholder('Jean');
    await firstNameField.click();
    await firstNameField.pressSequentially(firstName, { delay: 50 });
    await expect(firstNameField).toHaveValue(firstName);

    const lastNameField = page.getByPlaceholder('Dupont');
    await lastNameField.click();
    await lastNameField.pressSequentially(lastName, { delay: 50 });
    await expect(lastNameField).toHaveValue(lastName);

    const emailField = page.getByPlaceholder('you@example.com');
    await emailField.click();
    await emailField.pressSequentially(email, { delay: 50 });
    await expect(emailField).toHaveValue(email);

    const passwordField = page.locator('input[type="password"]');
    await passwordField.click();
    await passwordField.pressSequentially(password, { delay: 50 });
    await expect(passwordField).toHaveValue(password);

    // Submit form
    await page.getByRole('button', { name: /s'inscrire|sign up/i }).click();

    // NEW FLOW: Wait for redirect to device creation page for auto-created house
    await page.waitForURL(/\/fr\/houses\/[^/]+\/devices\/new/, { timeout: 10000 });

    // Verify we're on the add device page
    await expect(page.getByRole('heading', { name: /ajouter un appareil/i })).toBeVisible({ timeout: 10000 });

    // Verify the page contains device type selection
    await expect(page.getByLabel(/type d'appareil/i)).toBeVisible();
  });

  test('User can login after registration', async ({ page }) => {
    // First, register a user
    const timestamp = Date.now();
    const email = `testuser${timestamp}@houseflow.test`;
    const password = 'TestPassword123!'; // Meets requirements
    const firstName = 'Test';
    const lastName = 'User';

    await page.goto('http://localhost:3000/fr/register');

    // Fill registration form (webkit compatibility - use pressSequentially)
    const firstNameField = page.getByPlaceholder('Jean');
    await firstNameField.click();
    await firstNameField.pressSequentially(firstName, { delay: 50 });
    await expect(firstNameField).toHaveValue(firstName);

    const lastNameField = page.getByPlaceholder('Dupont');
    await lastNameField.click();
    await lastNameField.pressSequentially(lastName, { delay: 50 });
    await expect(lastNameField).toHaveValue(lastName);

    const emailField = page.getByPlaceholder('you@example.com');
    await emailField.click();
    await emailField.pressSequentially(email, { delay: 50 });
    await expect(emailField).toHaveValue(email);

    const passwordField = page.locator('input[type="password"]');
    await passwordField.click();
    await passwordField.pressSequentially(password, { delay: 50 });
    await expect(passwordField).toHaveValue(password);

    await page.getByRole('button', { name: /s'inscrire/i }).click();

    // Wait for device creation page after registration
    await page.waitForURL(/\/fr\/houses\/[^/]+\/devices\/new/);

    // Logout (simulate by clearing session storage and going to login)
    await page.evaluate(() => {
      sessionStorage.clear();
      // Clear the access token from memory
      if ((window as any).__setAccessToken) {
        (window as any).__setAccessToken(null);
      }
    });

    // Navigate to login page
    await page.goto('http://localhost:3000/fr/login');

    // Login with same credentials (webkit compatibility - use pressSequentially)
    const loginEmailField = page.getByPlaceholder('you@example.com');
    await loginEmailField.click();
    await loginEmailField.pressSequentially(email, { delay: 50 });
    await expect(loginEmailField).toHaveValue(email);

    const loginPasswordField = page.locator('input[type="password"]');
    await loginPasswordField.click();
    await loginPasswordField.pressSequentially(password, { delay: 50 });
    await expect(loginPasswordField).toHaveValue(password);

    await page.getByRole('button', { name: /se connecter|login/i }).click();

    // After login, user lands on dashboard
    await page.waitForURL(/\/fr\/dashboard/, { timeout: 10000 });

    // Verify dashboard shows "Ma maison" (auto-created house)
    await expect(page.getByText(/ma maison/i)).toBeVisible({ timeout: 10000 });

    // Click on the house to navigate to it
    await page.getByText(/ma maison/i).click();
    await page.waitForURL(/\/fr\/houses\/[^/]+$/, { timeout: 10000 });

    // Verify we're on the house page
    await expect(page.getByRole('heading', { name: /ma maison/i })).toBeVisible({ timeout: 10000 });
  });
});
