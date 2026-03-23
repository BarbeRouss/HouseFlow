import { test, expect } from '../fixtures/auth';
import { RegisterPage } from '../pages/register-page';
import { LoginPage } from '../pages/login-page';
import { DashboardPage } from '../pages/dashboard-page';
import { HousePage } from '../pages/house-page';

test.describe('User Flow 1: Onboarding (First Time Experience)', () => {
  test('Complete onboarding: Register -> Auto-created House -> Add Device', async ({ page }) => {
    // STEP 1: Registration
    const registerPage = new RegisterPage(page);
    await registerPage.goto();

    const email = `newuser-${Date.now()}@houseflow.test`;
    const password = 'SecurePass123!'; // Meets requirements: 12+ chars, uppercase, lowercase, digit, special char
    const firstName = 'Jean';
    const lastName = 'Dupont';

    await registerPage.register(firstName, lastName, email, password);
    await registerPage.expectRegisterSuccess();

    // STEP 2: After registration, user is redirected to device creation
    // for the auto-created house "Ma maison"
    await expect(page).toHaveURL(/\/fr\/houses\/[^/]+\/devices\/new/);

    // STEP 3: Verify we're on the add device page
    await expect(page.getByRole('heading', { name: /ajouter un appareil/i })).toBeVisible();

    // STEP 4: Add first device
    await page.getByLabel(/nom de l'appareil/i).fill('Chaudiere Principale');
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: 'Chaudière Gaz' }).click();
    await page.getByRole('button', { name: /save|enregistrer/i }).click();

    // STEP 5: Verify redirect to house page
    await expect(page).toHaveURL(/\/fr\/houses\/[^/]+$/);

    // STEP 6: Verify the auto-created house "Ma maison" exists
    await expect(page.getByRole('heading', { name: /ma maison/i })).toBeVisible();
    await expect(page.getByText('Chaudiere Principale')).toBeVisible();
  });

  test('Login after registration should work', async ({ page, testUser }) => {
    const API_URL = process.env.API_URL || 'http://localhost:5203';

    // Register user via API first
    const registerResponse = await page.request.post(`${API_URL}/api/v1/auth/register`, {
      data: testUser,
    });
    expect(registerResponse.ok()).toBeTruthy();

    // Now test login
    const loginPage = new LoginPage(page);
    await loginPage.goto();

    // Login with the test user
    await loginPage.login(testUser.email, testUser.password);
    await loginPage.expectLoginSuccess();

    // User should be auto-redirected to their house (since they have one house)
    // We just verify the login was successful via expectLoginSuccess above
  });

  test('Invalid login credentials should fail', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();

    await loginPage.login('nonexistent@test.com', 'WrongPassword!');
    await loginPage.expectLoginError();
  });

  test('Duplicate email registration should fail', async ({ page, testUser }) => {
    const API_URL = process.env.API_URL || 'http://localhost:5203';

    // Register user via API first
    const registerResponse = await page.request.post(`${API_URL}/api/v1/auth/register`, {
      data: testUser,
    });
    expect(registerResponse.ok()).toBeTruthy();

    // Now try to register again with the same email via UI
    const registerPage = new RegisterPage(page);
    await registerPage.goto();

    await registerPage.register('Another', 'User', testUser.email, 'DifferentPass123!');
    await registerPage.expectRegisterError();
  });
});
