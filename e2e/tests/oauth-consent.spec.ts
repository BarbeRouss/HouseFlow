import { createHash, randomBytes } from 'node:crypto';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { test as base, expect, APIRequestContext, Page } from '@playwright/test';
import { addRefreshCookie, registerViaApi } from '../fixtures/auth';
import { LoginPage } from '../pages/login-page';

const API_URL = process.env.API_URL || 'http://localhost:5203';
const PASSWORD = 'TestPassword123!';

/**
 * OAuth 2.1 authorization server (#304), as Claude uses it: Dynamic Client Registration, then the
 * authorization code flow with PKCE S256 through the HouseFlow login and consent screens, the token
 * endpoint, and the « Applications connectées » section of the account page.
 *
 * The client's redirect URI is a real loopback server (RFC 8252 §7.3, like a desktop MCP client) on
 * an ephemeral port: the API reaches it with a 302, and Playwright never hands a redirect hop to a
 * page.route() handler — only the first URL of a chain is routed.
 */
const test = base.extend<{ callback: { redirectUri: string } }>({
  callback: async ({}, use) => {
    const server = createServer((_req, res) => {
      res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', Connection: 'close' });
      res.end('<!doctype html><title>OAuth callback</title><p>callback</p>');
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const { port } = server.address() as AddressInfo;
    await use({ redirectUri: `http://127.0.0.1:${port}/callback` });
    server.closeAllConnections();
    await new Promise<void>((resolve) => server.close(() => resolve()));
  },
});

/** DCR (RFC 7591): a public client (no secret), as Claude registers itself. */
async function registerClient(request: APIRequestContext, name: string, redirectUri: string): Promise<string> {
  const res = await request.post(`${API_URL}/connect/register`, {
    data: { client_name: name, redirect_uris: [redirectUri] },
  });
  expect(res.status(), `DCR ${name}: ${await res.text()}`).toBe(201);
  const client = await res.json();
  expect(client.client_secret).toBeUndefined();
  expect(client.token_endpoint_auth_method).toBe('none');
  return client.client_id;
}

type AuthorizationRequest = { url: string; state: string; verifier: string };

/** GET /connect/authorize with PKCE S256 (RFC 7636): random verifier, SHA-256 challenge, random state. */
function authorizationRequest(clientId: string, redirectUri: string, scope = 'houses:read houses:write'): AuthorizationRequest {
  const verifier = randomBytes(32).toString('base64url');
  const challenge = createHash('sha256').update(verifier).digest('base64url');
  const state = randomBytes(16).toString('base64url');
  const query = new URLSearchParams({
    response_type: 'code',
    client_id: clientId,
    redirect_uri: redirectUri,
    scope,
    state,
    code_challenge: challenge,
    code_challenge_method: 'S256',
  });
  return { url: `${API_URL}/connect/authorize?${query}`, state, verifier };
}

/**
 * « Autoriser » is armed a moment after the consent screen shows (DoubleClickjacking): wait until
 * it is enabled, then click it.
 */
async function allow(page: Page) {
  const accept = page.getByTestId('oauth-accept');
  await expect(accept).toBeEnabled();
  await accept.click();
}

/** Waits for the browser to reach the client's redirect URI and returns its query (code / error, state). */
async function callbackParams(page: Page, redirectUri: string): Promise<URLSearchParams> {
  await page.waitForURL((url) => url.href.startsWith(`${redirectUri}?`), { timeout: 30_000 });
  return new URL(page.url()).searchParams;
}

function exchangeCode(request: APIRequestContext, clientId: string, redirectUri: string, code: string, verifier: string) {
  return request.post(`${API_URL}/connect/token`, {
    form: { grant_type: 'authorization_code', code, redirect_uri: redirectUri, client_id: clientId, code_verifier: verifier },
  });
}

function refreshTokens(request: APIRequestContext, clientId: string, refreshToken: string) {
  return request.post(`${API_URL}/connect/token`, {
    form: { grant_type: 'refresh_token', refresh_token: refreshToken, client_id: clientId },
  });
}

test.describe('OAuth — consent screen and connected apps (#304)', () => {
  test('login → consent → code issued and exchanged; a native app is asked again, without a new login', async ({ page, request, callback }) => {
    const user = await registerViaApi(request, { firstName: 'Oauth', lastName: 'Login', password: PASSWORD });
    const clientId = await registerClient(request, 'Claude E2E', callback.redirectUri);
    const auth = authorizationRequest(clientId, callback.redirectUri);

    // The application opens its authorization URL; nobody is signed in to HouseFlow in this browser:
    // API → /oauth/authorize?returnUrl= → /fr/oauth/authorize → P02, the request kept in the returnUrl.
    await page.goto(auth.url);
    await expect(page).toHaveURL(/\/fr\/login\?returnUrl=/, { timeout: 30_000 });
    await new LoginPage(page).login(user.email, PASSWORD);

    // Signed in: the oauthSession cookie is set and the request resumed → consent screen.
    const consent = page.getByTestId('oauth-consent');
    await expect(consent).toBeVisible({ timeout: 30_000 });
    await expect(page).toHaveURL(/\/fr\/oauth\/consent\?returnUrl=/);
    await expect(page.getByTestId('oauth-client-name')).toHaveText('Claude E2E');
    await expect(page.getByTestId('oauth-redirect-host')).toContainText(new URL(callback.redirectUri).host);
    await expect(page.getByTestId('oauth-scope-houses-read')).toBeChecked();
    await expect(page.getByTestId('oauth-scope-houses-write')).toBeChecked();
    await expect(consent).not.toContainText('offline_access');

    await allow(page);
    const params = await callbackParams(page, callback.redirectUri);
    expect(params.get('state')).toBe(auth.state);
    const code = params.get('code');
    expect(code, 'authorization code').toBeTruthy();

    const tokenRes = await exchangeCode(request, clientId, callback.redirectUri, code!, auth.verifier);
    expect(tokenRes.status(), await tokenRes.text()).toBe(200);
    const tokens = await tokenRes.json();
    expect(tokens.access_token).toBeTruthy();
    expect(tokens.refresh_token).toBeTruthy();
    expect(tokens.token_type).toBe('Bearer');
    expect(String(tokens.scope).split(' ')).toEqual(expect.arrayContaining(['houses:read', 'houses:write']));

    // A native application (loopback redirect URI) is never answered without the user: any local
    // process can reuse its client_id (RFC 8252 §8.6). A new request comes back to the consent
    // screen, ticked as before — but HouseFlow does not ask the user to log in again.
    const pages: string[] = [];
    page.on('framenavigated', (frame) => { if (frame === page.mainFrame()) pages.push(frame.url()); });
    const again = authorizationRequest(clientId, callback.redirectUri);
    await page.goto(again.url);
    await expect(consent).toBeVisible({ timeout: 30_000 });
    await expect(page.getByTestId('oauth-client-name')).toHaveText('Claude E2E');
    await expect(page.getByTestId('oauth-scope-houses-read')).toBeChecked();
    await expect(page.getByTestId('oauth-scope-houses-write')).toBeChecked();
    await allow(page);
    const second = await callbackParams(page, callback.redirectUri);
    expect(second.get('code'), 'code issued after the new consent').toBeTruthy();
    expect(second.get('state')).toBe(again.state);
    expect(pages.filter((url) => new URL(url).pathname.endsWith('/login'))).toEqual([]);
  });

  test('revoking from the account page cuts the application off', async ({ page, request, callback }) => {
    const user = await registerViaApi(request, { firstName: 'Oauth', lastName: 'Revoke' });
    await addRefreshCookie(page.context(), user.refreshCookie);
    const clientId = await registerClient(request, 'Claude E2E', callback.redirectUri);
    const auth = authorizationRequest(clientId, callback.redirectUri);

    await page.goto(auth.url);
    await expect(page.getByTestId('oauth-consent')).toBeVisible({ timeout: 30_000 });
    await allow(page);
    const code = (await callbackParams(page, callback.redirectUri)).get('code');
    expect(code, 'authorization code').toBeTruthy();
    const tokenRes = await exchangeCode(request, clientId, callback.redirectUri, code!, auth.verifier);
    expect(tokenRes.status(), await tokenRes.text()).toBe(200);

    // While connected, the refresh token works (and rotates).
    const rotated = await refreshTokens(request, clientId, (await tokenRes.json()).refresh_token);
    expect(rotated.status(), await rotated.text()).toBe(200);
    const currentRefreshToken = (await rotated.json()).refresh_token;

    await page.goto('/fr/settings#applications');
    const apps = page.getByTestId('connected-apps');
    await expect(apps).toContainText('Claude E2E', { timeout: 15_000 });
    // Where it receives access: the name is self-declared, the host is not.
    await expect(apps).toContainText(new URL(callback.redirectUri).host);
    await expect(apps).toContainText('Lecture et écriture');
    await apps.getByTestId('connected-app-revoke').click();
    const dialog = page.getByTestId('confirm-dialog');
    await expect(dialog).toContainText(/Révoquer Claude E2E\s?\?/);
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/oauth/authorizations/') && r.request().method() === 'DELETE'),
      dialog.getByTestId('confirm-action').click(),
    ]);
    await expect(page.getByTestId('connected-apps-empty')).toBeVisible({ timeout: 10_000 });

    // Revoked: the refresh token is refused, and the application has to ask for consent again.
    const refused = await refreshTokens(request, clientId, currentRefreshToken);
    expect(refused.status()).toBe(400);
    expect((await refused.json()).error).toBe('invalid_grant');
    await page.goto(authorizationRequest(clientId, callback.redirectUri).url);
    await expect(page.getByTestId('oauth-consent')).toBeVisible({ timeout: 30_000 });
  });

  test('« Refuser » sends access_denied back to the application, without a code', async ({ page, request, callback }) => {
    const user = await registerViaApi(request, { firstName: 'Oauth', lastName: 'Deny' });
    await addRefreshCookie(page.context(), user.refreshCookie);
    const clientId = await registerClient(request, 'Claude E2E Refus', callback.redirectUri);
    const auth = authorizationRequest(clientId, callback.redirectUri);

    await page.goto(auth.url);
    await expect(page.getByTestId('oauth-consent')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByTestId('oauth-client-name')).toHaveText('Claude E2E Refus');
    await page.getByTestId('oauth-deny').click();

    const params = await callbackParams(page, callback.redirectUri);
    expect(params.get('error')).toBe('access_denied');
    expect(params.get('state')).toBe(auth.state);
    expect(params.get('code')).toBeNull();

    // Nothing was granted.
    await page.goto('/fr/settings#applications');
    await expect(page.getByTestId('connected-apps-empty')).toBeVisible({ timeout: 15_000 });
  });

  test('consent in English: only the ticked scopes are granted', async ({ page, request, callback }) => {
    const user = await registerViaApi(request, { firstName: 'Oauth', lastName: 'English' });
    // The API's redirects carry no locale: the account language picks /en/.
    const settings = await request.put(`${API_URL}/api/v1/users/settings`, {
      headers: { Authorization: `Bearer ${user.token}` },
      data: { theme: 'system', language: 'en' },
    });
    expect(settings.ok(), `settings: HTTP ${settings.status()}`).toBeTruthy();
    await addRefreshCookie(page.context(), user.refreshCookie);
    const clientId = await registerClient(request, 'Claude E2E Partial', callback.redirectUri);
    const auth = authorizationRequest(clientId, callback.redirectUri);

    await page.goto(auth.url);
    await expect(page.getByTestId('oauth-consent')).toBeVisible({ timeout: 30_000 });
    await expect(page).toHaveURL(/\/en\/oauth\/consent\?returnUrl=/);
    await expect(page.getByTestId('oauth-accept')).toHaveText('Allow');
    await expect(page.getByTestId('oauth-deny')).toHaveText('Deny');

    await page.getByTestId('oauth-scope-houses-write').uncheck();
    await allow(page);
    const code = (await callbackParams(page, callback.redirectUri)).get('code');
    expect(code, 'authorization code').toBeTruthy();

    const tokenRes = await exchangeCode(request, clientId, callback.redirectUri, code!, auth.verifier);
    expect(tokenRes.status(), await tokenRes.text()).toBe(200);
    const scopes = String((await tokenRes.json()).scope).split(' ');
    expect(scopes).toContain('houses:read');
    expect(scopes).not.toContain('houses:write');
  });

  test('a returnUrl that is not the API authorization endpoint is never followed', async ({ page, request }) => {
    const user = await registerViaApi(request, { firstName: 'Oauth', lastName: 'Guard' });
    await addRefreshCookie(page.context(), user.refreshCookie);
    const oauthCalls: string[] = [];
    page.on('request', (r) => { if (r.url().includes('/api/v1/oauth/')) oauthCalls.push(`${r.method()} ${r.url()}`); });

    const foreign = 'https://evil.example/connect/authorize?client_id=x&scope=houses%3Aread';
    await page.goto(`/fr/oauth/authorize?returnUrl=${encodeURIComponent(foreign)}`);
    await expect(page.getByTestId('oauth-error')).toBeVisible({ timeout: 30_000 });
    await expect(page).toHaveURL(/\/fr\/oauth\/authorize\?returnUrl=/);

    await page.goto(`/fr/oauth/consent?returnUrl=${encodeURIComponent(`${API_URL}/api/v1/users/me`)}`);
    await expect(page.getByTestId('oauth-error')).toBeVisible({ timeout: 30_000 });
    await expect(page).toHaveURL(/\/fr\/oauth\/consent\?returnUrl=/);

    // No oauthSession opened, no application looked up.
    expect(oauthCalls).toEqual([]);
  });

  test('a redirect_uri the application did not register is never shown, nor consented to', async ({ page, request, callback }) => {
    const user = await registerViaApi(request, { firstName: 'Oauth', lastName: 'Redirect' });
    await addRefreshCookie(page.context(), user.refreshCookie);
    const clientId = await registerClient(request, 'Claude E2E Redirect', callback.redirectUri);
    const grants: string[] = [];
    page.on('request', (r) => {
      if (r.method() === 'POST' && r.url().includes('/api/v1/oauth/authorizations')) grants.push(r.url());
    });

    // Control: the same link with the redirect_uri the client registered shows the screen.
    const genuine = authorizationRequest(clientId, callback.redirectUri);
    await page.goto(`/fr/oauth/consent?returnUrl=${encodeURIComponent(genuine.url)}`);
    await expect(page.getByTestId('oauth-redirect-host')).toContainText(new URL(callback.redirectUri).host, { timeout: 30_000 });

    // A forged link: the API's own endpoint and a real client, but a redirect_uri this client never
    // registered — the screen would otherwise vouch for claude.ai on behalf of any application.
    const forged = authorizationRequest(clientId, 'https://claude.ai/api/mcp/auth_callback');
    await page.goto(`/fr/oauth/consent?returnUrl=${encodeURIComponent(forged.url)}`);
    const error = page.getByTestId('oauth-error');
    await expect(error).toBeVisible({ timeout: 30_000 });
    await expect(error).toHaveAttribute('data-reason', 'invalid');
    await expect(page.getByTestId('oauth-consent')).toHaveCount(0);
    await expect(page).toHaveURL(/\/fr\/oauth\/consent\?returnUrl=/);
    expect(grants).toEqual([]);
  });

  test('« Autoriser » is disarmed again whenever the window comes back to the front', async ({ page, request, callback }) => {
    const user = await registerViaApi(request, { firstName: 'Oauth', lastName: 'Armed' });
    await addRefreshCookie(page.context(), user.refreshCookie);
    const clientId = await registerClient(request, 'Claude E2E Armed', callback.redirectUri);

    await page.goto(authorizationRequest(clientId, callback.redirectUri).url);
    await expect(page.getByTestId('oauth-consent')).toBeVisible({ timeout: 30_000 });
    const accept = page.getByTestId('oauth-accept');
    await expect(accept).toBeEnabled();

    // DoubleClickjacking: the screen was loaded behind another window, which closes under the
    // first click of a double-click — the second one must not land on an armed button. Checked
    // 50 ms after the activation, well inside the 600 ms the button stays disarmed (timers fire in
    // order, so this check always runs before the button is armed again).
    const disarmed = await page.evaluate(async () => {
      window.dispatchEvent(new Event('focus'));
      await new Promise((resolve) => setTimeout(resolve, 50));
      return document.querySelector<HTMLButtonElement>('[data-testid="oauth-accept"]')?.disabled;
    });
    expect(disarmed, '« Autoriser » disarmed right after the window is activated').toBe(true);
    await expect(accept).toBeEnabled();
  });
});
