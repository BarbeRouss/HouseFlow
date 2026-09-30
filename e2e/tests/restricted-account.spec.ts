import { test, expect } from '@playwright/test';
import { addRefreshCookie, registerViaApi } from '../fixtures/auth';
import { restrictAccount } from '../fixtures/db';
import { LoginPage } from '../pages/login-page';

/**
 * RGPD Art. 18 — an account under processing restriction can neither log in nor refresh its
 * session: P02 shows the « compte suspendu » message (code `account_restricted`) with the privacy
 * contact, and never the generic « identifiants incorrects ».
 */
test.describe('Restricted account (Art. 18)', () => {
  test('Login shows the suspended-account message', async ({ page, request }) => {
    const password = 'TestPassword123!';
    const user = await registerViaApi(request, { firstName: 'Gel', lastName: 'Compte', password });
    restrictAccount(user.userId);

    const login = new LoginPage(page);
    await login.goto();
    await login.login(user.email, password);

    const error = login.errorMessage;
    await expect(error).toBeVisible();
    await expect(error).toHaveAttribute('data-code', 'account_restricted');
    await expect(error).toContainText('Votre compte est temporairement suspendu');
    await expect(error.getByRole('link')).toHaveAttribute('href', /^mailto:/);
    await expect(page).toHaveURL(/\/fr\/login/);
  });

  test('An open session restricted meanwhile ends on the login page with the same message', async ({ page, request }) => {
    const user = await registerViaApi(request, { firstName: 'Gel', lastName: 'Session' });
    restrictAccount(user.userId);

    // Boot refresh with the (still valid) refresh cookie → 401 account_restricted → P02.
    await addRefreshCookie(page.context(), user.refreshCookie);
    await page.goto('/fr/dashboard');

    await expect(page).toHaveURL(/\/fr\/login\?reason=restricted/);
    const error = page.getByTestId('login-error');
    await expect(error).toHaveAttribute('data-code', 'account_restricted');
    await expect(error).toContainText('Votre compte est temporairement suspendu');
  });
});
