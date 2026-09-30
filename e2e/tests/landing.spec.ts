import { test, expect, registerViaApi, createHouseViaApi, createInvitationViaApi } from '../fixtures/auth';
import { request as playwrightRequest } from '@playwright/test';

// P01 · Présentation (/{locale}): public entry point with a non-interactive P07 preview (DemoData).
test.describe('Landing page (P01)', () => {
  test('Guest sees the title, both actions and the demo preview', async ({ page }) => {
    await page.goto('/fr');

    await expect(page).toHaveURL(/\/fr\/?$/);
    await expect(page.getByTestId('landing-title')).toHaveText('Les entretiens de votre maison, au bon moment.');
    await expect(page.getByTestId('landing-register')).toHaveText('Créer un compte');
    await expect(page.getByTestId('landing-login')).toHaveText('Se connecter');
    await expect(page.getByTestId('landing-header-login')).toBeVisible();

    const frame = page.getByTestId('landing-preview-frame');
    await expect(frame).toHaveAttribute('aria-label', 'Aperçu du tableau de bord HouseFlow');

    const preview = page.getByTestId('landing-preview');
    await expect(preview).toHaveAttribute('inert', '');
    await expect(preview).toHaveAttribute('aria-hidden', 'true');

    // Desktop picture of P07 (the flat mobile version is hidden from 640 px).
    const rows = preview.locator('[data-testid="maintenance-row"]:visible');
    await expect(rows).toHaveCount(3);
    await expect(rows.nth(0)).toContainText('Ramonage');
    await expect(rows.nth(0)).toContainText('Poêle à bois · Chalet de Spa');
    await expect(rows.nth(0)).toContainText('En retard de 8 j');
    await expect(rows.nth(1)).toContainText('Entretien annuel');
    await expect(rows.nth(1)).toContainText('Chaudière gaz · Maison de Namur');
    await expect(rows.nth(1)).toContainText('Dans 12 j');
    await expect(rows.nth(2)).toContainText('Détecteur de fumée · Maison de Namur');
    await expect(rows.nth(2)).toContainText('Dans 26 j');
    await expect(preview.getByTestId('dashboard-title')).toContainText('3 entretiens à traiter');
    await expect(preview.locator('[data-testid="progress-ring"]:visible')).toContainText('8/11');
    // Device tiles carry the type tint (wood stove, gas boiler, smoke detector).
    await expect(preview.locator('.hf-device-tile:visible')).toHaveCount(3);
    await expect(preview.locator('.hf-device-tile:visible').nth(0)).toHaveAttribute('data-type', 'wood');

    // Non interactive: no ⋯ menu, no link, nothing focusable.
    await expect(preview.getByTestId('maintenance-row-menu')).toHaveCount(0);
    await expect(preview.locator('a')).toHaveCount(0);

    // No app header (C1) and no consent banner (C8) on P01.
    await expect(page.getByTestId('nav-home')).toHaveCount(0);
  });

  test('"/" shows the landing page in the default locale', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByTestId('landing-title')).toBeVisible();
  });

  test('English landing page', async ({ page }) => {
    await page.goto('/en');
    await expect(page.getByTestId('landing-title')).toHaveText("Your house's maintenance, right on time.");
    await expect(page.getByTestId('landing-preview').locator('[data-testid="maintenance-row"]:visible').first()).toContainText('8 days overdue');
  });

  test('"Créer un compte" goes to the registration page', async ({ page }) => {
    await page.goto('/fr');
    await page.getByTestId('landing-register').click();
    await expect(page).toHaveURL(/\/fr\/register$/);
  });

  test('"Se connecter" (button and header link) go to the login page', async ({ page }) => {
    await page.goto('/fr');
    await page.getByTestId('landing-login').click();
    await expect(page).toHaveURL(/\/fr\/login$/);

    await page.goto('/fr');
    await page.getByTestId('landing-header-login').click();
    await expect(page).toHaveURL(/\/fr\/login$/);
  });

  test('Mobile: flat mobile preview, stacked full-width actions, no header register button', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/fr');
    const preview = page.getByTestId('landing-preview');
    await expect(preview.locator('[data-testid="maintenance-row"]:visible')).toHaveCount(3);
    await expect(preview.getByTestId('dashboard-header')).toBeHidden();
    await expect(preview.getByTestId('dashboard-header-mobile')).toBeVisible();
    await expect(page.getByTestId('landing-header-register')).toBeHidden();
    const register = (await page.getByTestId('landing-register').boundingBox())!;
    const login = (await page.getByTestId('landing-login').boundingBox())!;
    expect(login.y).toBeGreaterThan(register.y);
    expect(Math.round(register.width)).toBe(Math.round(login.width));
    const hasHorizontalScroll = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(hasHorizontalScroll).toBe(false);
  });

  test('Signed-in user opening "/" or "/fr" goes to the dashboard', async ({ authenticatedPage: page }) => {
    await page.goto('/fr');
    await expect(page).toHaveURL(/\/fr\/dashboard/);
    await page.goto('/');
    await expect(page).toHaveURL(/\/fr\/dashboard/);
  });

  test('Logout from the account page (mobile, no avatar menu) lands on the landing page', async ({ authenticatedPage: page }) => {
    // P11 has no side menu any more; under 640 px « Se déconnecter » ends the account column.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/fr/settings');
    await page.getByTestId('settings-logout-mobile').click();
    await expect(page).toHaveURL(/\/fr\/?$/, { timeout: 10000 });
    await expect(page.getByTestId('landing-title')).toBeVisible();
  });
});

// AuthLayout: a signed-in user following an invitation link to P03 is sent to P04, not P07.
test('Signed-in user opening /register?invitation= goes to the invitation page', async ({ authenticatedPage: page }) => {
  const api = await playwrightRequest.newContext();
  const owner = await registerViaApi(api, { firstName: 'Owner', lastName: 'Landing' });
  const houseId = await createHouseViaApi(api, owner.token, 'Maison invitation');
  const invitation = await createInvitationViaApi(api, owner.token, houseId, 'CollaboratorRO');
  await api.dispose();

  await page.goto(`/fr/register?invitation=${encodeURIComponent(invitation.token)}`);
  await expect(page).toHaveURL(new RegExp(`/fr/invitations/${invitation.token}$`));
});

// AuthLayout: a signed-in user on /login?returnUrl=X (e.g. an old tab) goes to X, not P07.
test('Signed-in user opening /login?returnUrl= goes to that page', async ({ authenticatedPage: page }) => {
  await page.goto(`/fr/login?returnUrl=${encodeURIComponent('/fr/settings')}`);
  await expect(page).toHaveURL(/\/fr\/settings$/);
  // An unsafe returnUrl is ignored.
  await page.goto(`/fr/login?returnUrl=${encodeURIComponent('https://evil.example/')}`);
  await expect(page).toHaveURL(/\/fr\/dashboard$/);
});

// Links without a locale (old bookmarks): the same page under /fr, query kept; anything else is a 404.
test.describe('Routes without a locale', () => {
  test('Guest: /dashboard → /fr/dashboard → login with its returnUrl; /privacy → /fr/privacy', async ({ page }) => {
    await page.goto('/dashboard');
    await expect(page).toHaveURL(new RegExp(`/fr/login\\?returnUrl=${encodeURIComponent('/fr/dashboard')}$`));

    await page.goto('/privacy');
    await expect(page).toHaveURL(/\/fr\/privacy$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Politique de confidentialité' })).toBeVisible();

    await page.goto('/invitations/does-not-exist?accept=1');
    await expect(page).toHaveURL(/\/fr\/invitations\/does-not-exist\?accept=1$/);
    await expect(page.getByRole('heading', { name: "Cette invitation n'est plus valide" })).toBeVisible({ timeout: 15000 });
  });

  test('Signed in: /houses → /fr/houses; an unknown path stays a 404', async ({ authenticatedPage: page }) => {
    await page.goto('/houses');
    await expect(page).toHaveURL(/\/fr\/houses$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Maisons' })).toBeVisible();

    await page.goto('/not-a-page');
    await expect(page.getByTestId('error-page')).toHaveAttribute('data-code', '404');
    await page.goto('/fr/houses/x/y');
    await expect(page.getByTestId('error-page')).toHaveAttribute('data-code', '404');
  });
});

// R7: the small text links of the public pages are 44 px tall on mobile.
test('Mobile: header login link, footer links and the legal back link are 44 px tall', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/fr');
  expect((await page.getByTestId('landing-header-login').boundingBox())!.height).toBeGreaterThanOrEqual(44);
  const footerLinks = page.locator('footer a');
  for (let i = 0; i < await footerLinks.count(); i++) {
    expect((await footerLinks.nth(i).boundingBox())!.height).toBeGreaterThanOrEqual(44);
  }
  // P14/P15 public header (specs/ux): « Se connecter » is the mobile entry back into the app.
  await page.goto('/fr/terms');
  const login = page.getByTestId('legal-header-login');
  expect((await login.boundingBox())!.height).toBeGreaterThanOrEqual(44);
});
