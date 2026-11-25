import { test as base, expect, Page } from '@playwright/test';
import { addRefreshCookie, registerViaApi } from '../fixtures/auth';

const API_URL = process.env.API_URL || 'http://localhost:5203';
const AUTHORIZATIONS = '**/api/v1/oauth/authorizations';

/**
 * P11 « Applications connectées » → bloc « Connecter Claude » (#386): the MCP server address
 * (API origin + /mcp, derived by the front from its API base URL), « Copier », the steps.
 * The block never waits on the list of applications below.
 */
const test = base.extend<{ settings: Page }>({
  settings: async ({ page, request }, use) => {
    const user = await registerViaApi(request, { firstName: 'Connect', lastName: 'Claude' });
    await addRefreshCookie(page.context(), user.refreshCookie);
    await use(page);
  },
});

const block = (page: Page) => page.getByTestId('connect-claude');
const url = (page: Page) => page.getByTestId('mcp-server-url');
const copy = (page: Page) => page.getByTestId('mcp-server-url-copy');

test.describe('Connecter Claude — URL du serveur MCP', () => {
  test("l'adresse affichée est la ressource annoncée par le serveur, la liste vide renvoie au bloc", async ({ settings: page, request }) => {
    await page.goto('/fr/settings#applications');
    await expect(block(page)).toBeVisible({ timeout: 30_000 });

    const metadata = await request.get(`${API_URL}/.well-known/oauth-protected-resource`);
    expect(metadata.status(), await metadata.text()).toBe(200);
    const { resource } = await metadata.json();
    expect(resource).toMatch(/\/mcp$/);
    await expect(url(page)).toHaveValue(resource);
    await expect(url(page)).toHaveAttribute('readonly', '');
    await expect(page.getByLabel('Adresse du serveur MCP', { exact: true })).toHaveValue(resource);
    await expect(page.getByTestId('connect-claude-steps').locator('li')).toHaveCount(4);

    // Empty list: the existing message plus a pointer to the block above.
    const empty = page.getByTestId('connected-apps-empty');
    await expect(empty).toBeVisible({ timeout: 15_000 });
    await expect(empty).toContainText('Suivez les étapes ci-dessus pour connecter Claude');
  });

  test("le bloc reste affiché avec une liste remplie et avec une liste en erreur", async ({ settings: page }) => {
    await page.route(AUTHORIZATIONS, (route) =>
      route.request().method() === 'GET'
        ? route.fulfill({
            json: [{
              id: 'a1', clientId: 'c1', clientName: 'Claude E2E', redirectHosts: ['claude.ai'],
              scopes: ['houses:read'], createdAt: '2026-11-01T10:00:00Z',
            }],
          })
        : route.continue());
    await page.goto('/fr/settings#applications');
    await expect(page.getByTestId('connected-apps')).toContainText('Claude E2E', { timeout: 30_000 });
    await expect(block(page)).toBeVisible();
    await expect(url(page)).toHaveValue(/\/mcp$/);

    await page.unroute(AUTHORIZATIONS);
    await page.route(AUTHORIZATIONS, (route) => route.fulfill({ status: 500, json: { title: 'boom' } }));
    // Same URL (hash only): goto() would not reload the page, so the list would not be fetched again.
    await page.reload();
    await expect(page.getByRole('alert').filter({ hasText: 'Impossible de charger vos applications connectées' })).toBeVisible({ timeout: 30_000 });
    await expect(block(page)).toBeVisible();
    await expect(copy(page)).toBeEnabled();
  });

  test('« Copier » met l\'adresse dans le presse-papiers, « Copié » puis retour à « Copier »', async ({ settings: page }) => {
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.goto('/fr/settings#applications');
    await expect(copy(page)).toBeVisible({ timeout: 30_000 });
    await expect(copy(page)).toHaveAccessibleName(/Copier.*Adresse du serveur MCP/);
    await expect(copy(page)).toContainText('Copier');

    await copy(page).click();
    await expect(copy(page)).toContainText('Copié');
    await expect(page.getByTestId('mcp-server-url-status')).toContainText('copiée');
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(await url(page).inputValue());

    await expect(copy(page)).toContainText('Copier', { timeout: 6_000 });
    await expect(copy(page)).not.toContainText('Copié');
  });

  test('copie impossible : message discret, le champ reste sélectionnable', async ({ settings: page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: { writeText: () => Promise.reject(new DOMException('denied', 'NotAllowedError')) },
      });
    });
    await page.goto('/fr/settings#applications');
    await expect(copy(page)).toBeVisible({ timeout: 30_000 });

    await copy(page).click();
    await expect(page.getByTestId('mcp-server-url-status')).toContainText('Copie impossible, sélectionnez l\'adresse et copiez-la à la main');
    await expect(copy(page)).not.toContainText('Copié');

    await url(page).click();
    expect(await url(page).evaluate((el: HTMLInputElement) => el.value.slice(el.selectionStart ?? 0, el.selectionEnd ?? 0)))
      .toBe(await url(page).inputValue());
  });
});
