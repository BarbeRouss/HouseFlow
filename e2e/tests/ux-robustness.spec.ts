import { test, expect, Page, Route } from '@playwright/test';
import { SESSION_HINT_KEY } from '../fixtures/auth';
import { createDevice, createHouse, createType, monthsAgo, openAs, registerUser } from '../fixtures/maintenance-seed';

const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';
const REFRESH = '**/api/v1/auth/refresh';

/** CORS headers of the real API, so a fulfilled refresh answer is readable by the SPA. */
function cors(page: Page) {
  return {
    'Access-Control-Allow-Origin': new URL(page.url() || FRONTEND_URL).origin,
    'Access-Control-Allow-Credentials': 'true',
  };
}

async function seedDashboard(request: Parameters<typeof registerUser>[0]) {
  const s = await registerUser(request, 'Robust', 'User');
  const houseId = await createHouse(request, s, 'Maison robuste');
  const deviceId = await createDevice(request, s, houseId, { name: 'Chaudière', type: 'Chaudière Gaz' });
  await createType(request, s, deviceId, { name: 'Entretien annuel', lastMaintenance: monthsAgo(14) });
  return { s, houseId, deviceId };
}

// ---------------------------------------------------------------------------
// Session robustness: only a 401 on the refresh ends the session.
// ---------------------------------------------------------------------------
test.describe('Session robustness (boot refresh)', () => {
  test('8 reloads within a minute keep the session', async ({ page, request }) => {
    const { s } = await seedDashboard(request);
    await openAs(page, s, '/fr/dashboard');
    await expect(page.locator('header').getByText('RU')).toBeVisible({ timeout: 15000 });
    for (let i = 0; i < 8; i++) {
      await page.reload();
      await expect(page.locator('header').getByText('RU')).toBeVisible({ timeout: 15000 });
      await expect(page).toHaveURL(/\/fr\/dashboard$/);
    }
  });

  test('A throttled, failing or unreachable refresh is retried, not taken for a logout', async ({ page, request }) => {
    const { s } = await seedDashboard(request);
    await openAs(page, s, '/fr/dashboard');
    await expect(page.locator('header').getByText('RU')).toBeVisible({ timeout: 15000 });

    // Next boot: 429 (Retry-After 1 s) → 503 → network error → the real API.
    let calls = 0;
    await page.route(REFRESH, async (route: Route) => {
      calls++;
      if (calls === 1) {
        return route.fulfill({
          status: 429, contentType: 'application/problem+json',
          headers: { ...cors(page), 'Retry-After': '1' },
          body: JSON.stringify({ status: 429, code: 'rate_limited', retryAfter: 1 }),
        });
      }
      if (calls === 2) return route.fulfill({ status: 503, headers: cors(page), body: '' });
      if (calls === 3) return route.abort('failed');
      return route.continue();
    });

    await page.reload();
    await expect(page.locator('header').getByText('RU')).toBeVisible({ timeout: 30000 });
    await expect(page).toHaveURL(/\/fr\/dashboard$/);
    expect(calls).toBe(4);
    expect(await page.evaluate((k) => localStorage.getItem(k), SESSION_HINT_KEY)).toBe('1');
  });

  test('A refused refresh (401) ends the session: login page', async ({ page, request }) => {
    const { s } = await seedDashboard(request);
    await openAs(page, s, '/fr/dashboard');
    await expect(page.locator('header').getByText('RU')).toBeVisible({ timeout: 15000 });

    await page.route(REFRESH, (route) => route.fulfill({
      status: 401, contentType: 'application/problem+json', headers: cors(page),
      body: JSON.stringify({ status: 401, code: 'invalid_refresh_token' }),
    }));
    // (openAs' init script re-sets the hint on every load, so only the redirect is asserted.)
    await page.reload();
    await expect(page).toHaveURL(/\/fr\/login/, { timeout: 15000 });
  });
});

// ---------------------------------------------------------------------------
// Mobile long press on a C3 row (decisions 11 / 20)
// ---------------------------------------------------------------------------
test.describe('Mobile long press', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true });

  test('The ⋯ menu opened by a long press stays open after the finger lifts', async ({ page, request, context }) => {
    const { s } = await seedDashboard(request);
    await openAs(page, s, '/fr/dashboard');
    const row = page.getByTestId('maintenance-row').first();
    await expect(row).toBeVisible({ timeout: 15000 });
    const box = (await row.boundingBox())!;

    const cdp = await context.newCDPSession(page);
    const point = { x: box.x + 40, y: box.y + box.height / 2 };
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [point] });
    await page.waitForTimeout(900);
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    await page.waitForTimeout(700);

    // Still open, still on P07 (the lift neither closed the menu nor followed the row link).
    const item = page.getByTestId('menu-done-other-date');
    await expect(item).toBeVisible();
    await expect(page).toHaveURL(/\/fr\/dashboard$/);

    // The next real tap works: « Fait à une autre date… » opens M3.
    await item.tap();
    await expect(page.getByTestId('record-modal')).toBeVisible();
  });
});

// ---------------------------------------------------------------------------
// Modal focus (M5, C « Modale »): focus kept inside, Esc always closes, back to the opener.
// ---------------------------------------------------------------------------
test.describe('Modal focus', () => {
  test('M5: after « Inviter » the copy button has the focus; after cancelling an invitation the focus stays inside; Esc closes and returns to the opener', async ({ page, request }) => {
    const { s, houseId } = await seedDashboard(request);
    await openAs(page, s, `/fr/houses/${houseId}`);

    const menu = page.getByTestId('house-menu');
    await menu.click();
    await page.getByTestId('house-menu-members').click();
    const modal = page.getByTestId('members-modal');
    await expect(modal).toBeVisible();

    await modal.getByTestId('invite-email').fill(`invitee-${Date.now()}@houseflow.test`);
    await modal.getByTestId('invite-submit').click();
    await expect(modal.getByTestId('invitation-link-box')).toBeVisible();
    await expect(modal.getByTestId('invitation-link-copy')).toBeFocused();

    // Cancel the invitation from its ⋯ menu: the row (and its menu) disappear.
    await modal.getByTestId('invitation-menu').click();
    await modal.getByTestId('invitation-cancel').click();
    await expect(modal.getByTestId('invitation-row')).toHaveCount(0);
    expect(await modal.evaluate((el) => el.contains(document.activeElement))).toBeTruthy();

    await page.keyboard.press('Escape');
    await expect(modal).toHaveCount(0);
    await expect(menu).toBeFocused();
  });
});
